using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime
{
    // Runtime extension API: modules are stateless; per-encounter state belongs to Context.State.
    public abstract class BossComponentPlugin
    {
        public virtual Dictionary<string,string> DefaultParameters => new Dictionary<string,string>();
        public virtual void ObserveIncoming(BossComponentContext context,DamageInfo original) { }
        public virtual void AdaptIncoming(BossComponentContext context,ref DamageInfo damage) { }
        public virtual BossAttackReservation PrepareOutgoing(BossComponentContext context,Thing target,DamageInfo damage) => null;
        public virtual void Tick(BossComponentContext context,Thing target) { }
        public virtual IEnumerable<Gizmo> Gizmos(BossComponentContext context) { yield break; }
    }
    public sealed class BossComponentContext
    {
        public readonly BossRecord Record;
        public readonly BossComponentSpec Spec;
        public readonly IDictionary<string,string> State;
        public Pawn Pawn => Record.Proxy;
        public Map Map => Pawn.Map;
        public bool Available => Pawn.Spawned&&!Pawn.Dead&&!Record.Ended&&Record.Active&&Record.FallenUntil<0;
        public BossComponentContext(BossRecord record,BossComponentSpec spec)
        {
            Record=record;Spec=spec;
            string key="component:"+spec.Id;
            if(!record.ModuleState.TryGetValue(key,out var state))record.ModuleState[key]=state=new Dictionary<string,string>();
            State=state;
        }
        public double Number(string key,double fallback) => Spec.Parameters!=null&&Spec.Parameters.TryGetValue(key,out var s)&&double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:fallback;
        public double Read(string key,double fallback=0) => State.TryGetValue(key,out var s)&&double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:fallback;
        public void Write(string key,double value) => State[key]=value.ToString("R",CultureInfo.InvariantCulture);
    }
    public sealed class BossAttackReservation
    {
        public double Bonus;
        // Called exactly once, including failed/absorbed/exceptional attacks.
        public Action<bool> Complete;
    }
    public static class BossComponents
    {
        static readonly Dictionary<string,BossComponentPlugin> plugins=new Dictionary<string,BossComponentPlugin>();
        [ThreadStatic] static int suppressOutgoing;
        public static bool OutgoingSuppressed => suppressOutgoing>0;
        public static IDisposable SuppressOutgoing(){suppressOutgoing++;return new Scope();}
        sealed class Scope : IDisposable {bool disposed;public void Dispose(){if(!disposed){disposed=true;suppressOutgoing--;}}}
        public static void Visit(BossRecord record,Action<BossComponentPlugin,BossComponentContext> action)
        {
            foreach(var spec in record.Template.Components??new List<BossComponentSpec>()){
                if(!spec.Enabled)continue;
                var context=new BossComponentContext(record,spec);
                if(context.Read("framework.disabled")!=0)continue;
                try{
                    if(!plugins.TryGetValue(spec.Module,out var plugin)){
                        plugin=Modules.Create(spec.Module) as BossComponentPlugin;
                        if(plugin==null)throw new InvalidOperationException("Missing BossComponent plugin: "+spec.Module);
                        plugins[spec.Module]=plugin;
                    }
                    action(plugin,context);
                }catch(Exception ex){context.Write("framework.disabled",1);Log.Error("[RimCampaign] Boss component disabled ("+spec.Module+"): "+ex);}
            }
        }
        public static DamageInfo Incoming(BossRecord record,DamageInfo damage)
        {
            // All observers receive the same original value, regardless of component order.
            var original=damage;
            Visit(record,(plugin,context)=>plugin.ObserveIncoming(context,original));
            Visit(record,(plugin,context)=>plugin.AdaptIncoming(context,ref damage));
            return damage;
        }
        public static IEnumerable<Gizmo> Gizmos(BossRecord record)
        {
            var result=new List<Gizmo>();
            Visit(record,(plugin,context)=>result.AddRange(plugin.Gizmos(context)));
            return result;
        }
    }
    [CampaignModule("boss.adaptation","BossComponent","Sigmoid 伤害适应（低伤害不变，高伤害平滑压缩）")]
    public sealed class BossSigmoidAdaptation : BossComponentPlugin
    {
        public override Dictionary<string,string> DefaultParameters => new Dictionary<string,string>{{"threshold","100"},{"ceiling","500"}};
        public override void AdaptIncoming(BossComponentContext context,ref DamageInfo damage)
        {
            string name=damage.Def?.defName;
            if((context.Spec.DamageExceptions??new List<DefRef>()).Any(d=>d.Kind=="DamageDef"&&d.Name==name))return;
            damage.SetAmount((float)BossAdaptation.Compress(damage.Amount,context.Number("threshold",100),context.Number("ceiling",500)));
        }
    }
    // A data-only preset can attach framework bosses outside campaign maps.
    public class BossTemplateDef : Def { public string templateJson; }
    public class BossPawnExtension : DefModExtension { public BossTemplateDef template; }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.SpawnSetup))]
    static class BossPresetSpawn
    {
        static void Postfix(Pawn __instance,bool respawningAfterLoad)
        {
            if(respawningAfterLoad)return;
            var preset=__instance.kindDef?.GetModExtension<BossPawnExtension>()?.template;
            if(preset!=null)__instance.Map.GetComponent<BossMapComponent>().Attach(__instance,Json.Read<BossTemplate>(preset.templateJson),"preset:"+__instance.thingIDNumber);
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.GetGizmos))]
    static class BossComponentGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result,Pawn __instance)
        {
            foreach(var g in __result)yield return g;
            var record=BossMapComponent.For(__instance);
            if(record!=null&&(__instance.Faction==Faction.OfPlayer||Prefs.DevMode))foreach(var g in BossComponents.Gizmos(record))yield return g;
        }
    }
    [HarmonyPatch(typeof(Thing),nameof(Thing.TakeDamage))]
    static class BossOutgoingDamage
    {
        [ThreadStatic] static HashSet<BossRecord> pending;
        sealed class Attempt
        {
            public BossRecord Attacker,TargetBoss;
            public double HealthBefore;
            public bool Completed;
            public readonly List<BossAttackReservation> Reservations=new List<BossAttackReservation>();
            public void Finish(bool success){if(Completed)return;Completed=true;pending?.Remove(Attacker);foreach(var r in Reservations)try{r.Complete?.Invoke(success);}catch(Exception ex){Log.Error("[RimCampaign] Attack completion failed: "+ex);}}
        }
        static void Prefix(Thing __instance,ref DamageInfo dinfo,out Attempt __state)
        {
            __state=null;
            if(BossComponents.OutgoingSuppressed||dinfo.Amount<=0||dinfo.Instigator==__instance)return;
            var record=BossMapComponent.For(dinfo.Instigator as Pawn);
            if(record==null||!record.Active||record.FallenUntil>=0)return;
            pending??=new HashSet<BossRecord>();
            if(!pending.Add(record))return;
            var attempt=new Attempt{Attacker=record,TargetBoss=BossMapComponent.For(__instance as Pawn)};
            attempt.HealthBefore=attempt.TargetBoss?.Health??0;
            __state=attempt;
            var original=dinfo;
            BossComponents.Visit(record,(plugin,context)=>{var r=plugin.PrepareOutgoing(context,__instance,original);if(r!=null)attempt.Reservations.Add(r);});
            double bonus=attempt.Reservations.Sum(r=>double.IsFinite(r.Bonus)?Math.Max(0,r.Bonus):0);
            dinfo.SetAmount((float)Math.Min(float.MaxValue,(double)dinfo.Amount+bonus));
        }
        static void Postfix(DamageWorker.DamageResult __result,Attempt __state)
        {
            if(__state==null)return;
            __state.Finish(__result.totalDamageDealt>0||(__state.TargetBoss!=null&&__state.TargetBoss.Health<__state.HealthBefore));
        }
        static Exception Finalizer(Exception __exception,Attempt __state){__state?.Finish(false);return __exception;}
    }
}
