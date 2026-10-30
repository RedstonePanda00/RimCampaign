using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimCampaign.Runtime
{
    public sealed class BossRecord : IExposable
    {
        public string Id, TemplateJson;
        public Pawn Proxy;
        public double Health;
        public int Phase;
        public bool Ended, HealthLocked;
        public bool Active=true;
        public int FallenUntil=-1;
        public List<int> PendingPhases=new List<int>();
        public Dictionary<string,int> Cooldowns = new Dictionary<string,int>();
        public Dictionary<string,Dictionary<string,string>> ModuleState = new Dictionary<string,Dictionary<string,string>>();
        private string stateJson;
        private BossTemplate template;
        public BossTemplate Template => template ?? (template = Json.Read<BossTemplate>(TemplateJson));
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id,"id"); Scribe_Values.Look(ref TemplateJson,"template");
            Scribe_References.Look(ref Proxy,"proxy"); Scribe_Values.Look(ref Health,"health");
            Scribe_Values.Look(ref HealthLocked,"healthLocked");
            Scribe_Values.Look(ref Active,"active",true);Scribe_Values.Look(ref FallenUntil,"fallenUntil",-1);
            Scribe_Collections.Look(ref PendingPhases,"pendingPhases",LookMode.Value);
            if(Scribe.mode==LoadSaveMode.PostLoadInit&&PendingPhases==null)PendingPhases=new List<int>();
            Scribe_Values.Look(ref Phase,"phase"); Scribe_Values.Look(ref Ended,"ended");
            if(Scribe.mode==LoadSaveMode.Saving)stateJson=Json.Write(new BossSavedState{Cooldowns=Cooldowns,Modules=ModuleState});
            Scribe_Values.Look(ref stateJson,"behaviorState");
            if(Scribe.mode==LoadSaveMode.PostLoadInit){
                var state=string.IsNullOrEmpty(stateJson)?new BossSavedState():Json.Read<BossSavedState>(stateJson);
                Cooldowns=state.Cooldowns;ModuleState=state.Modules;template=null;
            }
        }
    }
    public class BossSavedState
    {
        public Dictionary<string,int> Cooldowns { get; set; } = new Dictionary<string,int>();
        public Dictionary<string,Dictionary<string,string>> Modules { get; set; } = new Dictionary<string,Dictionary<string,string>>();
    }
    // Exactly one independently generated Pawn occupies one cell and acts as the attack target.
    // Its normal renderer/verbs/ability caster remain usable; encounter HP never uses body-part HP.
    public class BossMapComponent : MapComponent
    {
        // Encounter adapters can constrain AI without changing a reusable boss template.
        public static event Func<Pawn,bool> BehaviorPermission;
        public static event Func<Pawn,Thing,bool> TargetPermission;
        public static bool BehaviorsAllowed(Pawn p)=>BehaviorPermission==null||BehaviorPermission.GetInvocationList().Cast<Func<Pawn,bool>>().All(f=>f(p));
        public static bool TargetAllowed(Pawn p,Thing t)=>TargetPermission==null||TargetPermission.GetInvocationList().Cast<Func<Pawn,Thing,bool>>().All(f=>f(p,t));
        public List<BossRecord> Records = new List<BossRecord>();
        readonly Dictionary<string,IBossBehaviorModule> modules = new Dictionary<string,IBossBehaviorModule>();
        readonly Dictionary<Pawn,BossRecord> byPawn = new Dictionary<Pawn,BossRecord>();
        public BossMapComponent(Map map):base(map) { }
        public static BossRecord For(Pawn pawn)
        {
            var owner=pawn?.Map?.GetComponent<BossMapComponent>();
            return owner!=null&&owner.byPawn.TryGetValue(pawn,out var record)&&!record.Ended?record:null;
        }
        public override void FinalizeInit(){base.FinalizeInit();byPawn.Clear();foreach(var r in Records)if(r.Proxy!=null&&!r.Ended)byPawn[r.Proxy]=r;}
        public BossRecord Attach(Pawn pawn,BossTemplate template,string id)
        {
            if(byPawn.TryGetValue(pawn,out var existing))return existing;
            if(template==null||!double.IsFinite(template.MaxHealth)||template.MaxHealth<=0||template.Phases.Count==0)throw new InvalidOperationException("Boss needs finite health and phases.");
            var record=new BossRecord{Id=id,Proxy=pawn,TemplateJson=Json.Write(template),Health=template.MaxHealth,Active=template.InitiallyActive,HealthLocked=template.Phases[0].LockHealth,PendingPhases=new List<int>{0}};
            Records.Add(record);byPawn[pawn]=record;return record;
        }
        public Pawn Spawn(BossTemplate template, IntVec3 cell, Faction faction, string id, Pawn source=null, Profile profile=null)
        {
            if(template==null||template.MaxHealth<=0||template.Phases.Count==0)throw new InvalidOperationException("Boss needs health and phases.");
            if(Records.Any(r=>r.Id==id&&!r.Ended))throw new InvalidOperationException("Boss already active: "+id);
            Pawn copy;
            if(source!=null) {
                var snapshot=Profiles.Capture(source);
                // Third-party state is handled by explicit import modules, not transferred by reference.
                if(snapshot.PortabilityErrors.Count>0)Log.Warning("[RimCampaign] Boss copied standard Pawn data; custom component state requires BossPawnImport modules: "+string.Join("; ",snapshot.PortabilityErrors.Distinct()));
                snapshot.PortabilityErrors.Clear();snapshot.Relations.Clear();
                var data=Json.Read<PawnData>(snapshot.Payload);
                var injuries=source.health.hediffSet.hediffs.Where(h=>h is Hediff_Injury||h is Hediff_MissingPart).Select(h=>h.def.defName).ToHashSet();
                data.Health.RemoveAll(h=>injuries.Contains(h.Def));snapshot.Payload=Json.Write(data);
                copy=Profiles.Create(snapshot,faction);
            } else copy=profile!=null?Profiles.Create(profile,faction):Profiles.Generate(template.PawnKind,faction);
            try {
                if(copy.def.size!=IntVec2.One)throw new InvalidOperationException("Boss proxies require a one-cell Pawn race.");
                if(source!=null)foreach(var module in Modules.All.Where(d=>d.Category=="BossPawnImport"))
                    ((IBossPawnImportModule)Modules.Create(module.Id)).Import(source,copy);
                Attach(copy,template,id);
                GenSpawn.Spawn(copy,cell,map);
                copy.jobs.StopAll();if(copy.playerSettings!=null)copy.playerSettings.hostilityResponse=HostilityResponseMode.Ignore;
                return copy;
            } catch {Records.RemoveAll(r=>r.Proxy==copy);byPawn.Remove(copy);if(!copy.Destroyed)copy.Destroy(DestroyMode.Vanish);throw;}
        }
        public void ReceiveHit(BossRecord record, DamageInfo damage)
        {
            if(record.Ended||!record.Active||record.FallenUntil>=0)return;
            damage=BossComponents.Incoming(record,damage);
            var change=BossHealth.Apply(record.Template,record.Health,record.Phase,record.HealthLocked,damage.Amount*Math.Max(0,record.Template.DamageMultiplier));
            var previous=record.Health;
            record.Health=change.Health;record.Phase=change.Phase;record.HealthLocked=change.Locked;
            CampaignController.ForMap(map)?.RecordTriggerDamage(record.Proxy,previous-record.Health);
            foreach(int entered in change.Entered){record.PendingPhases.Add(entered);Signal("BossPhase",record);}
            if(change.Entered.Count>0)record.Proxy.jobs.StopAll();
            FlushPhases(record);
            if(record.Health<=0){
                if(record.Template.ReviveAfterSeconds>0){
                    record.FallenUntil=Find.TickManager.TicksGame+(int)(record.Template.ReviveAfterSeconds*60);
                    record.Proxy.jobs.StopAll();record.Proxy.health.forceDowned=true;
                    record.Proxy.health.AddHediff(DefDatabase<HediffDef>.GetNamed("RC_BossIncapacitated"));
                    Signal("BossDowned",record);
                }else{record.Ended=true;Signal("BossDefeated",record);record.Proxy.Kill(damage);}
            }
        }
        void FlushPhases(BossRecord record) {
            var controller=CampaignController.ForMap(map);
            while(record.PendingPhases.Count>0&&controller!=null&&controller.QueueBossPhase(map,record,record.PendingPhases[0]))record.PendingPhases.RemoveAt(0);
        }

        void Signal(string kind,BossRecord record) => CampaignController.ForMap(map)?.EmitBossSignal(map,kind,record.Id);
        public override void MapComponentTick()
        {
            int tick=Find.TickManager.TicksGame;
            foreach(var record in Records.Where(r=>!r.Ended).ToList()) {
                FlushPhases(record);
                var pawn=record.Proxy;
                if(pawn==null||pawn.Destroyed||pawn.Dead||!pawn.Spawned||pawn.Map!=map){record.Ended=true;Signal("BossRemoved",record);continue;}
                if(tick%60==0&&pawn.needs!=null)foreach(var need in pawn.needs.AllNeeds)need.CurLevelPercentage=1;
                if(record.FallenUntil>=0){
                    if(tick>=record.FallenUntil){
                        record.FallenUntil=-1;record.Health=record.Template.MaxHealth;pawn.health.forceDowned=false;
                        var fallen=pawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("RC_BossIncapacitated"));
                        if(fallen!=null)pawn.health.RemoveHediff(fallen);
                        pawn.health.CheckForStateChange(null,null);Signal("BossRevived",record);
                    }
                    continue;
                }
                if(!record.Active)continue;
                if(tick%60==0&&record.Template.RegenerationPerSecond>0)record.Health=Math.Min(record.Template.MaxHealth,record.Health+record.Template.RegenerationPerSecond);
                if(tick%6!=0)continue;
                if(!BehaviorsAllowed(pawn))continue;
                var fixedTarget=string.IsNullOrEmpty(record.Template.TargetRole)?null:CampaignController.ForMap(map)?.ResolveTriggerPawn(record.Template.TargetRole);
                Thing target=fixedTarget?.Spawned==true&&!fixedTarget.Dead&&!fixedTarget.Downed&&TargetAllowed(pawn,fixedTarget)?fixedTarget:map.attackTargetsCache.GetPotentialTargetsFor(pawn).Select(t=>t.Thing)
                    .Where(t=>t!=pawn&&t.Spawned&&t.HostileTo(pawn)&&!t.Fogged()&&(!(t is Pawn p)||!p.Dead&&!p.Downed)&&TargetAllowed(pawn,t)).OrderBy(t=>t.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
                BossComponents.Visit(record,(plugin,context)=>plugin.Tick(context,target));
                if(pawn.Faction==Faction.OfPlayer)continue;
                if(target==null)continue;
                var actions=record.Template.Phases[record.Phase].Behaviors;
                foreach(var action in actions) {
                    string key=record.Phase+":"+action.Id;
                    if(record.Cooldowns.TryGetValue(key,out int until)&&tick<until)continue;
                    try {
                        if(!modules.TryGetValue(action.Module,out var module)) {
                            module=Modules.Create(action.Module) as IBossBehaviorModule;
                            if(module==null)throw new InvalidOperationException("Missing BossBehavior module: "+action.Module);
                            modules[action.Module]=module;
                        }
                        if(!record.ModuleState.TryGetValue(key,out var state))record.ModuleState[key]=state=new Dictionary<string,string>();
                        if(module.Execute(action,new BossContext(record,target,state)))record.Cooldowns[key]=(int)Math.Min(int.MaxValue,(long)tick+Math.Max(1,action.CooldownTicks));
                    } catch(Exception ex) {
                        // Disable only this behavior for the encounter; preserve other bosses and the save.
                        record.Cooldowns[key]=int.MaxValue;
                        Log.Error("[RimCampaign] Boss behavior disabled ("+record.Id+" / "+action.Module+"): "+ex);
                    }
                }
            }
        }
        public override void ExposeData(){base.ExposeData();Scribe_Collections.Look(ref Records,"campaignBosses",LookMode.Deep);if(Records==null)Records=new List<BossRecord>();}
        // Encounter gates (such as waiting for a checkpoint) also define whether
        // the boss is active for presentation, not just whether its AI may act.
        public static bool ShouldShowHealthBar(BossRecord boss)=>boss!=null&&!boss.Ended&&boss.Active&&boss.Proxy?.Spawned==true&&BehaviorsAllowed(boss.Proxy);
        public override void MapComponentOnGUI()
        {
            if(Find.CurrentMap!=map)return;
            float y=72;
            foreach(var boss in Records.Where(ShouldShowHealthBar)) {
                var rect=new Rect(UI.screenWidth*.3f,y,UI.screenWidth*.4f,42);
                Widgets.DrawBoxSolid(rect,new Color(.08f,.09f,.12f,.85f));
                Widgets.DrawBoxSolid(new Rect(rect.x+8,rect.y+28,rect.width-16,7),Color.black);
                Widgets.DrawBoxSolid(new Rect(rect.x+8,rect.y+28,(rect.width-16)*(float)(boss.Health/boss.Template.MaxHealth),7),new Color(.72f,.15f,.12f));
                Text.Anchor=TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(rect.x,rect.y,rect.width,27),RcText.T(boss.Template.Name)+"  "+boss.Health.ToString("N0")+" / "+boss.Template.MaxHealth.ToString("N0")+"  ·  "+(boss.FallenUntil>=0?RcText.T("重新站起：")+Math.Ceiling((boss.FallenUntil-Find.TickManager.TicksGame)/60f)+"s":RcText.T(boss.Template.Phases[boss.Phase].Name))+(boss.HealthLocked?RimCampaign.Runtime.RcText.T("  [锁血]"):""));
                Text.Anchor=TextAnchor.UpperLeft;y+=48;
            }
        }
        public override void MapRemoved(){Records.Clear();modules.Clear();byPawn.Clear();}
    }
    public sealed class BossContext : IBossContext
    {
        readonly BossRecord record;readonly Thing target;
        public BossContext(BossRecord record,Pawn target,IDictionary<string,string> state):this(record,(Thing)target,state){}
        public BossContext(BossRecord record,Thing target,IDictionary<string,string> state){this.record=record;this.target=target;State=state;}
        public object Pawn=>record.Proxy; public object Target=>target; public object Map=>record.Proxy.Map;
        public double Health=>record.Health;public double MaxHealth=>record.Template.MaxHealth;
        public int Phase=>record.Phase;public int Tick=>Find.TickManager.TicksGame;
        public IDictionary<string,string> State{get;}
        bool InRange(float range)=>target!=null&&record.Proxy.Position.DistanceTo(target.Position)<=range&&GenSight.LineOfSight(record.Proxy.Position,target.Position,record.Proxy.Map);
        public bool Approach(float stopRange)
        {
            var pawn=record.Proxy;if(InRange(stopRange)||pawn.stances.FullBodyBusy)return false;
            if(pawn.CurJob?.def==JobDefOf.Goto&&pawn.CurJob.targetA.Cell==target.Position)return false;
            if(!pawn.CanReach(target,PathEndMode.Touch,Danger.Deadly))return false;
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto,target.Position),JobCondition.InterruptForced);return true;
        }
        public bool FireWeapon(float range)
        {
            var pawn=record.Proxy;if(!InRange(range)||pawn.stances.FullBodyBusy)return false;
            var verb=pawn.TryGetAttackVerb(target,false);
            if(verb==null||!verb.CanHitTarget(target))return false;
            pawn.jobs.StopAll();return verb.TryStartCastOn(target);
        }
        public bool CastAbility(string defName,float range)
        {
            var pawn=record.Proxy;if(!InRange(range)||pawn.stances.FullBodyBusy)return false;
            var ability=pawn.abilities?.abilities.FirstOrDefault(a=>a.def.defName==defName);
            if(ability==null||!ability.CanCast||!ability.CanApplyOn(new LocalTargetInfo(target)))return false;
            pawn.jobs.StopAll();return ability.Activate(target,target);
        }
        public bool DamageTarget(float damage,float range)
        {
            if(!InRange(range))return false;
            target.TakeDamage(new DamageInfo(DamageDefOf.Blunt,Math.Max(0,damage),instigator:record.Proxy));return true;
        }
    }
    [CampaignModule("boss.approach","BossBehavior","接近目标")]
    public class BossApproach : IBossBehaviorModule { public bool Execute(BossAction a,IBossContext c)=>c.Approach(a.Range); }
    [CampaignModule("boss.weapon","BossBehavior","使用载体武器／近战")]
    public class BossWeapon : IBossBehaviorModule { public bool Execute(BossAction a,IBossContext c)=>c.FireWeapon(a.Range); }
    [CampaignModule("boss.ability","BossBehavior","使用导入的原生能力")]
    public class BossAbility : IBossBehaviorModule { public bool Execute(BossAction a,IBossContext c)=>c.CastAbility(a.Ability?.Name,a.Range); }
    [CampaignModule("boss.damage","BossBehavior","范围内标准伤害")]
    public class BossDamage : IBossBehaviorModule { public bool Execute(BossAction a,IBossContext c)=>c.DamageTarget(a.Power,a.Range); }

    [HarmonyPatch(typeof(Pawn),nameof(Pawn.PreApplyDamage))]
    static class BossDamageRouting
    {
        static bool Prefix(Pawn __instance,DamageInfo dinfo,ref bool absorbed)
        {var r=BossMapComponent.For(__instance);if(r==null)return true;absorbed=true;__instance.Map.GetComponent<BossMapComponent>().ReceiveHit(r,dinfo);return false;}
    }
    [HarmonyPatch(typeof(Pawn_JobTracker),"DetermineNextJob")]
    static class BossThinking
    {
        static bool Prefix(Pawn ___pawn,ref ThinkTreeDef __0,ref ThinkResult __result)
        {if(___pawn.Faction==Faction.OfPlayer||BossMapComponent.For(___pawn)==null)return true;__0=null;__result=ThinkResult.NoJob;return false;}
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.Kill))]
    static class BossIndependentLife
    {
        static bool Prefix(Pawn __instance)=>BossMapComponent.For(__instance)==null;
    }
}
