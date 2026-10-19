using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace RimCampaign.Core
{
    public class Signal
    {
        public string RuleId { get; set; } = "";
        public string Kind { get; set; } = ""; public string Target { get; set; } = "";
        public string Source { get; set; } = "";
    }
    public class Fiber
    {
        public List<Act> Steps { get; set; } = new List<Act>();
        public int Index { get; set; }
        public double Remaining { get; set; } = -1;
        public bool FailureContext { get; set; }
        public string Source { get; set; } = "";
        public List<Fiber> Children { get; set; } = new List<Fiber>();
        public bool Done => Index >= Steps.Count;
    }
    public class DamageSample { public int Tick { get; set; } public double Amount { get; set; } }
    public class RuleMemory
    {
        public string HostId { get; set; } = "";
        public bool Dead { get; set; } public bool Downed { get; set; }
        public Dictionary<string,bool> Inside { get; set; } = new Dictionary<string,bool>();
        public List<DamageSample> Damage { get; set; } = new List<DamageSample>();
        public void AddDamage(int tick,double amount) {
            if(amount<=0||double.IsNaN(amount)||double.IsInfinity(amount))return;
            var last=Damage.LastOrDefault();if(last!=null&&last.Tick==tick)last.Amount+=amount;else Damage.Add(new DamageSample{Tick=tick,Amount=amount});
        }
        public bool ConsumeDamage(int tick,double seconds,double threshold) {
            Damage.RemoveAll(s=>s.Tick<tick-seconds*60);
            if(Damage.Sum(s=>s.Amount)<threshold)return false;
            Damage.Clear();return true;
        }
    }
    public class RunState
    {
        public Dictionary<string,RuleMemory> Triggers { get; set; } = new Dictionary<string,RuleMemory>();
        [OnDeserialized] void RestoreTriggers(StreamingContext context){if(Triggers==null)Triggers=new Dictionary<string,RuleMemory>();}
        public string Phase { get; set; } = "Running";
        public double Elapsed { get; set; }
        public Dictionary<string, double> Variables { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, string> Objectives { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, int> Firings { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, bool> Enabled { get; set; } = new Dictionary<string, bool>();
        public Dictionary<string, double> Timers { get; set; } = new Dictionary<string, double>();
        public List<Fiber> Fibers { get; set; } = new List<Fiber>();
        public List<Signal> Signals { get; set; } = new List<Signal>();
        public bool WantFailure { get; set; } public bool WantSuccess { get; set; }
    }
    public interface IHost
    {
        double Read(string key);
        // Return false for an asynchronous effect that is still running.
        bool Execute(Act action, string eventSource, double delta);
    }
    public interface IConditionHost { bool Evaluate(Condition condition); }
    public sealed class Engine
    {
        public RunState State { get; }
        readonly MapSpec map; readonly IHost host;
        public Engine(MapSpec map, RunState state, IHost host) { this.map = map; State = state; this.host = host; }
        public void Emit(string kind, string target = "", string source = "") => State.Signals.Add(new Signal { Kind = kind, Target = target, Source = source });
        public static bool NeedsHost(string kind)=>new[]{"Death","Downed","DamageWindow","BossPhase","BossDefeated","BossRemoved"}.Contains(kind);
        public void EmitForRule(string rule,string kind,string source="")=>State.Signals.Add(new Signal{RuleId=rule,Kind=kind,Source=source});
        public void Start()
        {
            State.Variables = new Dictionary<string, double>(map.Inputs);
            foreach (var o in map.Objectives) State.Objectives[o.Id] = "Active";
            Enqueue(map.Entry); Emit("Entry");
        }
        static List<Act> Instantiate(List<Act> actions) {
            var copy=Json.Copy(actions);
            foreach(var a in Validation.Actions(copy))a.Id=Guid.NewGuid().ToString("N");
            return copy;
        }
        public void Enqueue(List<Act> actions, bool failure = false, string source = "") => State.Fibers.Add(new Fiber { Steps = Instantiate(actions), FailureContext = failure, Source = source });
        public double Read(string key) => State.Variables.TryGetValue(key, out var n) ? n : host.Read(key);
        public bool Test(Condition c)
        {
            if (c == null) return true;
            switch (c.Op) {
                case "Always": return true; case "And": return c.Children.All(Test); case "Or": return c.Children.Any(Test);
                case "Not": return c.Children.Count == 1 && !Test(c.Children[0]);
                case "Equal": return Read(c.Key) == c.Value; case "NotEqual": return Read(c.Key) != c.Value;
                case "Greater": return Read(c.Key) > c.Value; case "AtLeast": return Read(c.Key) >= c.Value;
                case "Less": return Read(c.Key) < c.Value; case "AtMost": return Read(c.Key) <= c.Value;
                default: return host is IConditionHost extension ? extension.Evaluate(c) : throw new InvalidOperationException("Unknown condition: " + c.Op);
            }
        }
        public void Tick(double gameDelta, double presentationDelta)
        {
            if (State.Phase == "Defeated" || State.Phase == "Succeeded") return;
            if (State.Phase == "Running") {
                double previous = State.Elapsed; State.Elapsed += gameDelta;
                foreach (var timer in State.Timers.Keys.ToList()) {
                    State.Timers[timer] -= gameDelta;
                    if (State.Timers[timer] <= 0) { State.Timers.Remove(timer); Emit("Timer", timer); }
                }
                var events = State.Signals.ToList(); State.Signals.Clear();
                foreach (var rule in map.Rules) {
                    bool enabled = State.Enabled.TryGetValue(rule.Id, out var e) ? e : rule.Enabled;
                    if (!enabled || (rule.Once && State.Firings.ContainsKey(rule.Id))) continue;
                    var matches = events.Where(s => (string.IsNullOrEmpty(s.RuleId)||s.RuleId==rule.Id) &&
                        (!NeedsHost(s.Kind)||(!string.IsNullOrEmpty(rule.Host)&&s.RuleId==rule.Id)) &&
                        (s.Kind!="RangeEnter"&&s.Kind!="RangeLeave"||s.RuleId==rule.Id) &&
                        rule.Events.Any(ev => ev.Kind == s.Kind && (s.RuleId==rule.Id||ev.Target == "" || ev.Target == s.Target))).ToList();
                    bool timed = rule.Events.Any(ev => ev.Kind == "Elapsed" &&
                        ((previous < ev.Seconds && State.Elapsed >= ev.Seconds) || (ev.Seconds == 0 && events.Any(s => s.Kind == "Entry"))));
                    if(timed)matches.Add(new Signal { Kind="Elapsed" });
                    foreach(var matched in matches) {
                        if(!Test(rule.When))continue;
                        State.Firings[rule.Id] = State.Firings.TryGetValue(rule.Id, out var count) ? count + 1 : 1;
                        Enqueue(rule.Actions, source: matched.Source);
                        if(rule.Once)break;
                    }
                }
                foreach (var o in map.Objectives.Where(o => o.Mode == "Survive")) {
                    if (host.Read("dead:" + o.Role) > 0) {
                        if(!State.Objectives.TryGetValue(o.Id,out var status)||status!="Failed") {
                            State.Objectives[o.Id] = "Failed";Emit("Objective",o.Id,o.Role);
                        }
                        if (o.Required) State.WantFailure = true;
                    }
                }
            }
            // Resolve terminal intents after every active rule received the same event batch.
            foreach (var f in State.Fibers.ToList()) {
                if(State.Phase=="Running"&&State.WantFailure)break;
                if (State.Phase != "Running" && !f.FailureContext) continue;
                Advance(f, gameDelta, presentationDelta, 0);
            }
            State.Fibers.RemoveAll(f => f.Done);
            if (State.Phase == "Running" && State.WantFailure) {
                State.Phase = "Failing"; State.Fibers.Clear(); State.Signals.Clear();
                Enqueue(map.Defeat, true);
            } else if (State.Phase == "Running" && State.WantSuccess && State.Fibers.Count == 0) {
                State.Phase = "Succeeded";
                foreach (var o in map.Objectives.Where(o => o.Mode == "Survive" && State.Objectives[o.Id] == "Active")) State.Objectives[o.Id] = "Complete";
            }
            if (State.Phase == "Failing" && State.Fibers.Count == 0) State.Phase = "Defeated";
        }
        void Advance(Fiber f, double game, double presentation, int depth)
        {
            if(State.WantFailure&&!f.FailureContext)return;
            if (depth > 32) throw new InvalidOperationException("Action nesting exceeds 32.");
            double availableGame = game, availablePresentation = presentation;
            int budget = 1024;
            while (!f.Done && --budget > 0) {
                var a = f.Steps[f.Index];
                var dt = (a.Freeze || f.FailureContext) ? availablePresentation : availableGame;
                if (f.Children.Count > 0) {
                    foreach (var child in f.Children) Advance(child, availableGame, availablePresentation, depth + 1);
                    if (f.Children.Any(c => !c.Done)) return;
                    f.Children.Clear(); f.Index++; availableGame = availablePresentation = 0; continue;
                }
                if (a.Kind == "Wait") {
                    if (f.Remaining < 0) f.Remaining = Math.Max(0, a.Value);
                    var used = Math.Min(dt, f.Remaining); f.Remaining -= used;
                    if (a.Freeze || f.FailureContext) availablePresentation -= used; else availableGame -= used;
                    if (f.Remaining > 0) return;
                } else if (a.Kind == "If" || a.Kind == "Sequence" || a.Kind == "Parallel" || a.Kind == "Group") {
                    var steps = a.Kind == "If" ? (Test(a.When) ? a.Steps : a.Else) :
                        a.Kind == "Group" ? map.Groups.Single(g => g.Id == a.Target).Actions : a.Steps;
                    steps=Instantiate(steps);
                    if(a.Freeze||f.FailureContext)foreach(var childAction in Validation.Actions(steps))childAction.Freeze=true;
                    if (a.Kind == "Parallel") f.Children = steps.Select(s => new Fiber { Steps = new List<Act> { s }, Source = f.Source, FailureContext = f.FailureContext }).ToList();
                    else if (steps.Count > 0) f.Children.Add(new Fiber { Steps = Instantiate(steps), Source = f.Source, FailureContext = f.FailureContext });
                    if (f.Children.Count > 0) continue;
                } else if (a.Kind == "Set" || a.Kind == "Add") {
                    State.Variables[a.Target] = a.Kind == "Set" ? a.Value : Read(a.Target) + a.Value; Emit("Variable", a.Target, f.Source);
                } else if (a.Kind == "Objective") { State.Objectives[a.Target] = a.Text; Emit("Objective", a.Target, f.Source); }
                else if (a.Kind == "Timer") State.Timers[a.Target] = a.Value;
                else if (a.Kind == "Enable") State.Enabled[a.Target] = a.Value != 0;
                else if (a.Kind == "Fail") State.WantFailure = true;
                else if (a.Kind == "Win") State.WantSuccess = true;
                else if (!host.Execute(a, f.Source, dt)) return;
                f.Remaining = -1; f.Index++;
                if(State.WantFailure&&!f.FailureContext)return;
            }
            if (budget == 0) throw new InvalidOperationException("Action budget exceeded (possible recursive group).");
        }
        public Edge Next(Campaign c, string node, string result) => c.Edges.Where(e => e.From == node && e.Result == result && Test(e.When)).OrderByDescending(e => e.Priority).FirstOrDefault();
    }
}
