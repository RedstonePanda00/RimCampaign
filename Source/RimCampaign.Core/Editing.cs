using System;
using System.Collections.Generic;
using System.Linq;

namespace RimCampaign.Core
{
    public sealed class History<T>
    {
        readonly Stack<string> undo = new Stack<string>(), redo = new Stack<string>();
        public T Value { get; private set; }
        public History(T value) { Value = value; }
        public void Replace(T value) { undo.Push(Json.Write(Value));redo.Clear();Value=value; }
        public void Edit(Action<T> edit) {
            string before = Json.Write(Value);
            try { edit(Value); } catch { Value = Json.Read<T>(before); throw; }
            if (Json.Write(Value) != before) { undo.Push(before); redo.Clear(); }
        }
        public void Undo() { if (undo.Count == 0) return; redo.Push(Json.Write(Value)); Value = Json.Read<T>(undo.Pop()); }
        public void CommitAppliedChange(string before) { if(before!=null&&Json.Write(Value)!=before){undo.Push(before);redo.Clear();} }
        public void Redo() { if (redo.Count == 0) return; undo.Push(Json.Write(Value)); Value = Json.Read<T>(redo.Pop()); }
        public static List<Placement> CopyObjects(IEnumerable<Placement> objects, int dx, int dz) {
            var copy = Json.Copy(objects.ToList());
            foreach (var p in copy) { p.Id = Guid.NewGuid().ToString("N"); p.Position.X += dx; p.Position.Z += dz; }
            return copy;
        }
    }
    public class Issue { public string Path { get; set; } public string Message { get; set; } public override string ToString() => Path + ": " + Message; }
    public static class Validation
    {
        public static List<Issue> Check(Project p)
        {
            var issues = new List<Issue>();
            void Error(string path, string message) => issues.Add(new Issue { Path = path, Message = message });
            if (p.Version != 1) Error("Version", "Unsupported project version.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(p.PackageId ?? "", @"^[a-zA-Z0-9_-]+(\.[a-zA-Z0-9_-]+)+$")) Error("PackageId", "Use a stable dotted package ID.");
            if (p.PackageId == "rimcampaign.framework") Error("PackageId", "Campaign must have its own package ID.");
            foreach (var duplicate in p.Maps.Cast<Named>().Concat(p.Profiles).Concat(p.Bosses??new List<BossTemplate>()).Concat(p.Campaigns).GroupBy(x => x.Id).Where(g => g.Count() > 1)) Error(duplicate.Key, "Duplicate asset ID.");
            foreach (var profile in p.Profiles) {
                foreach (var error in profile.PortabilityErrors) Error(profile.Id, error);
                if (profile.GenerateFrom == null && string.IsNullOrWhiteSpace(profile.Payload)) Error(profile.Id, "Character profile is empty.");
                if (profile.GenerateFrom != null && (profile.GenerateFrom.Kind != "PawnKindDef" || string.IsNullOrWhiteSpace(profile.GenerateFrom.Name))) Error(profile.Id, "Select a PawnKindDef for random generation.");
                if (profile.References.Any(r => r.Package.Equals("void.charactereditor", StringComparison.OrdinalIgnoreCase))) Error(profile.Id, "Profile still references Character Editor content.");
                foreach(var relation in profile.Relations)
                    if(!p.Profiles.Any(x=>x.Id==relation.TargetProfile)||relation.TargetProfile==profile.Id)Error(profile.Id,"Bind relation "+relation.Relation+" to another profile.");
            }
            foreach(var boss in p.Bosses??new List<BossTemplate>()) {
                if(double.IsNaN(boss.MaxHealth)||double.IsInfinity(boss.MaxHealth)||boss.MaxHealth<=0)Error(boss.Id,"Boss health must be finite and positive.");
                if(boss.Phases.Count==0||boss.Phases[0].HealthFraction!=1)Error(boss.Id,"Boss needs an initial phase at health fraction 1.");
                double previous=2;
                foreach(var phase in boss.Phases) {
                    if(double.IsNaN(phase.HealthFraction)||phase.HealthFraction<=0||phase.HealthFraction>1||phase.HealthFraction>=previous)Error(boss.Id,"Boss phase fractions must strictly descend within (0, 1].");
                    previous=phase.HealthFraction;
                    if(phase.Behaviors.Select(a=>a.Id).Distinct().Count()!=phase.Behaviors.Count)Error(boss.Id,"Duplicate boss behavior IDs.");
                    foreach(var action in phase.Behaviors) {
                        if(string.IsNullOrWhiteSpace(action.Module)||action.CooldownTicks<1||float.IsNaN(action.Range)||float.IsInfinity(action.Range)||action.Range<0||float.IsNaN(action.Power)||float.IsInfinity(action.Power)||action.Power<0)Error(boss.Id,"Invalid boss behavior parameters.");
                        if(action.Module=="boss.ability"&&(action.Ability?.Kind!="AbilityDef"||string.IsNullOrWhiteSpace(action.Ability.Name)))Error(boss.Id,"Native ability behavior needs an AbilityDef.");
                    }
                }
                if(string.IsNullOrEmpty(boss.Profile)&&(boss.PawnKind?.Kind!="PawnKindDef"||string.IsNullOrWhiteSpace(boss.PawnKind.Name)))Error(boss.Id,"Select a fallback PawnKind for the boss.");
                if(!string.IsNullOrEmpty(boss.Profile)&&!p.Profiles.Any(a=>a.Id==boss.Profile))Error(boss.Id,"Missing boss source profile.");
            }
            foreach (var m in p.Maps) {
                if(m.Factions.Count(f=>f.Id=="Player")!=1)Error(m.Id,"Map needs exactly one Player faction.");
                if(m.Factions.Any(f=>string.IsNullOrWhiteSpace(f.Id)||f.Id=="Neutral"||string.IsNullOrWhiteSpace(f.Name))||m.Factions.GroupBy(f=>f.Id).Any(g=>g.Count()>1))Error(m.Id,"Invalid or duplicate map faction IDs/names.");
                if(m.Factions.Any(f=>!new[]{"Ally","Hostile","Neutral"}.Contains(f.Side)||(f.Id=="Player"&&f.Side!="Ally")))Error(m.Id,"Invalid map faction side.");
                if (m.Width < 20 || m.Height < 20 || m.Width > 500 || m.Height > 500) Error(m.Id, "Map dimensions must be 20..500.");
                var named = m.Objects.Cast<Named>().Concat(m.Regions).Concat(m.Rules).Concat(m.Objectives).Concat(m.Groups).ToList();
                foreach (var g in named.GroupBy(n => n.Id).Where(g => g.Count() > 1)) Error(m.Id + "/" + g.Key, "Duplicate object ID.");
                foreach (var o in m.Objects) {
                    if(o.Faction!="Neutral"&&!m.Factions.Any(f=>f.Id==o.Faction))Error(m.Id+"/"+o.Id,"Unknown map faction.");
                    if (o.Position.X < 0 || o.Position.Z < 0 || o.Position.X >= m.Width || o.Position.Z >= m.Height) Error(m.Id + "/" + o.Id, "Object outside map.");
                    if (o.Kind == "Pawn" && !m.Roles.Contains(o.Role)) Error(m.Id + "/" + o.Id, "Unknown role: " + o.Role);
                    if (o.Kind == "PawnKind" && (o.Def?.Kind != "PawnKindDef" || string.IsNullOrWhiteSpace(o.Def.Name))) Error(m.Id + "/" + o.Id, "PawnKind placement needs a PawnKindDef.");
                    if(o.Kind=="Boss"&&!(p.Bosses??new List<BossTemplate>()).Any(b=>b.Id==o.Boss))Error(m.Id+"/"+o.Id,"Missing Boss template.");
                    if(o.Kind=="Boss"&&!string.IsNullOrEmpty(o.BindRole)&&!m.Roles.Contains(o.BindRole))Error(m.Id+"/"+o.Id,"Unknown Boss binding role.");
                }
                foreach (var r in m.Regions)
                    if (r.Min.X > r.Max.X || r.Min.Z > r.Max.Z || r.Min.X < 0 || r.Min.Z < 0 || r.Max.X >= m.Width || r.Max.Z >= m.Height) Error(m.Id + "/" + r.Id, "Invalid region.");
                foreach (var r in m.Rules) {
                    if(!string.IsNullOrEmpty(r.Host)&&!m.Objects.Any(o=>o.Id==r.Host&&new[]{"Pawn","PawnKind","Boss"}.Contains(o.Kind))&&!m.Roles.Any(role=>r.Host=="role:"+role))Error(m.Id+"/"+r.Id,"Unknown trigger host.");
                    if(float.IsNaN(r.Radius)||float.IsInfinity(r.Radius)||r.Radius<=0||r.Radius>1000)Error(m.Id+"/"+r.Id,"Trigger radius must be within (0, 1000].");
                    if(r.Events.Any(e=>e.Kind=="DamageWindow")&&(double.IsNaN(r.DamageWindowSeconds)||double.IsInfinity(r.DamageWindowSeconds)||r.DamageWindowSeconds<=0||r.DamageWindowSeconds>3600||double.IsNaN(r.DamageThreshold)||double.IsInfinity(r.DamageThreshold)||r.DamageThreshold<=0))Error(m.Id+"/"+r.Id,"Invalid trigger damage window/threshold.");
                    if (r.Events.Count == 0) Error(m.Id + "/" + r.Id, "Trigger needs an event.");
                    CheckCondition(r.When, r.Id, Error);
                }
                var groupGraph = m.Groups.ToDictionary(g => g.Id, g => Actions(g.Actions).Where(a => a.Kind == "Group").Select(a => a.Target).ToList());
                CheckCycles(groupGraph, m.Id, Error);
                foreach (var a in Actions(m.Entry.Concat(m.Defeat).Concat(m.Rules.SelectMany(r => r.Actions)).Concat(m.Groups.SelectMany(g => g.Actions)).Concat((p.Bosses??new List<BossTemplate>()).Where(b=>m.Objects.Any(o=>o.Kind=="Boss"&&o.Boss==b.Id)).SelectMany(b=>b.Phases.SelectMany(f=>f.OnEnter))))) {
                    if(a.Kind=="Spawn"&&!m.Objects.Any(o=>o.Id==a.Target))Error(m.Id+"/"+a.Id,"Missing spawn placement.");
                    if(a.Kind=="SpawnGroup"&&(string.IsNullOrEmpty(a.Target)||!m.Objects.Any(o=>o.Group==a.Target)))Error(m.Id+"/"+a.Id,"Missing placement group.");
                    if(a.Kind=="RemoveTerrain"&&!m.Objects.Any(o=>o.Id==a.Target)&&!m.Regions.Any(o=>o.Id==a.Target))Error(m.Id+"/"+a.Id,"Missing terrain removal region/placement.");
                    if(a.Kind=="UnlockBoss"&&a.Target!="$source"&&!m.Objects.Any(o=>o.Id==a.Target&&o.Kind=="Boss"))Error(m.Id+"/"+a.Id,"Missing Boss unlock target.");
                    if (a.Value < 0 && a.Kind == "Wait") Error(m.Id + "/" + a.Id, "Wait must be nonnegative.");
                    if (a.Kind == "Group" && !m.Groups.Any(g => g.Id == a.Target)) Error(m.Id + "/" + a.Id, "Missing action group.");
                    if (a.Kind == "Objective" && !m.Objectives.Any(o => o.Id == a.Target)) Error(m.Id + "/" + a.Id, "Missing objective.");
                    if (a.Kind == "Enable" && !m.Rules.Any(r => r.Id == a.Target)) Error(m.Id + "/" + a.Id, "Missing trigger.");
                    CheckCondition(a.When, a.Id, Error);
                }
            }
            foreach (var c in p.Campaigns) {
                if (c.Nodes.Select(n => n.Id).Distinct().Count() != c.Nodes.Count) { Error(c.Id, "Duplicate node IDs."); continue; }
                if (!c.Nodes.Any(n => n.Id == c.Start)) Error(c.Id, "Missing start node.");
                foreach (var n in c.Nodes) {
                    var m = p.Maps.FirstOrDefault(x => x.Id == n.Map);
                    if (m == null && n.Kind != "Open") Error(c.Id + "/" + n.Id, "Missing map.");
                    if(m!=null)foreach(var resource in n.Resources)if(resource.Faction!="Neutral"&&!m.Factions.Any(f=>f.Id==resource.Faction))Error(c.Id+"/"+n.Id,"Unknown resource map faction.");
                    if (m != null) foreach (var role in m.Roles) {
                        if (!n.Roles.TryGetValue(role, out var id) || !p.Profiles.Any(x => x.Id == id)) Error(c.Id + "/" + n.Id + "/" + role, "Bind role to a profile.");
                    }
                }
                foreach (var e in c.Edges) {
                    if (!c.Nodes.Any(n => n.Id == e.From) || !c.Nodes.Any(n => n.Id == e.To)) Error(c.Id + "/" + e.Id, "Missing edge endpoint.");
                    CheckCondition(e.When, e.Id, Error);
                }
                var graph = c.Nodes.ToDictionary(n => n.Id, n => c.Edges.Where(e => e.From == n.Id).Select(e => e.To).ToList());
                CheckCycles(graph, c.Id, Error);
                var seen = new HashSet<string>(); var pending = new Stack<string>(); pending.Push(c.Start);
                while (pending.Count > 0) { var id = pending.Pop(); if (!seen.Add(id) || !graph.ContainsKey(id)) continue; foreach (var next in graph[id]) pending.Push(next); }
                foreach (var n in c.Nodes.Where(n => !seen.Contains(n.Id))) Error(c.Id + "/" + n.Id, "Unreachable node.");
            }
            return issues;
        }
        static void CheckCondition(Condition c, string path, Action<string,string> error) {
            if (c == null) return;
            if (c.Op == "Not" && c.Children.Count != 1) error(path, "NOT requires one condition.");
            foreach (var child in c.Children) CheckCondition(child, path, error);
        }
        public static IEnumerable<Act> Actions(IEnumerable<Act> actions) {
            foreach (var a in actions) { yield return a; foreach (var b in Actions(a.Steps.Concat(a.Else))) yield return b; }
        }
        static void CheckCycles(Dictionary<string,List<string>> graph, string path, Action<string,string> error) {
            var visiting = new HashSet<string>(); var done = new HashSet<string>();
            bool Visit(string id) {
                if (visiting.Contains(id)) return true;
                if (!graph.ContainsKey(id) || done.Contains(id)) return false;
                visiting.Add(id); bool cycle = graph[id].Any(Visit); visiting.Remove(id); done.Add(id); return cycle;
            }
            if (graph.Keys.Any(Visit)) error(path, "Cycle detected.");
        }
    }
}
