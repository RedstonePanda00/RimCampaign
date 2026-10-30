using System;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimCampaign.Runtime {
    [CampaignModule("rc.joinActor","Action","Move Target pawn within Value cells of Text pawn, using native movement.")]
    public sealed class JoinActor:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){
            var c=(CampaignController)host;var pawn=c.ResolveTriggerPawn(a.Target);var other=c.ResolveTriggerPawn(a.Text);
            if(pawn?.Spawned!=true||other?.Spawned!=true||pawn.Map!=c.Map||other.Map!=c.Map||pawn.Dead||pawn.Downed)return true;
            float radius=Math.Max(1,(float)a.Value);
            if(pawn.Position.DistanceTo(other.Position)<=radius&&GenSight.LineOfSight(pawn.Position,other.Position,c.Map)){pawn.jobs.StopAll();return true;}
            if(pawn.CurJob?.def==JobDefOf.Goto&&pawn.CurJob.targetA.Cell.DistanceTo(other.Position)<=radius)return false;
            var choices=GenRadial.RadialCellsAround(other.Position,radius,true).Where(at=>at.InBounds(c.Map)&&at!=other.Position&&at.Standable(c.Map)&&pawn.CanReach(at,Verse.AI.PathEndMode.OnCell,Danger.Deadly)).OrderBy(at=>at.DistanceToSquared(pawn.Position)).ToList();
            if(choices.Count==0)return false;
            var job=JobMaker.MakeJob(JobDefOf.Goto,choices[0]);job.playerForced=true;pawn.jobs.StartJob(job,Verse.AI.JobCondition.InterruptForced);return false;
        }
    }
    [CampaignModule("rc.fireAtWill","Action","Set drafted pawn automatic fire: Target pawn, Value 0/1.")]
    public sealed class ActorFireAtWill:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;var pawn=c.ResolveTriggerPawn(a.Target);if(pawn?.Spawned==true&&pawn.Map==c.Map&&pawn.drafter!=null)pawn.drafter.FireAtWill=a.Value!=0;return true;}
    }
    [CampaignModule("rc.openingTitle","Action","Start the node's opening title at a scripted time.")]
    public sealed class StartOpeningTitle:IActionModule {public bool Execute(Act a,IHost host,string source,double delta){((CampaignController)host).BeginOpeningTitle();return true;}}
    [CampaignModule("rc.bossActive","Action","Set encounter active state; Target placement ID, Value 0/1.")]
    public sealed class BossActivation:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;var boss=c.Map.GetComponent<BossMapComponent>().Records.Single(r=>r.Id==a.Target&&!r.Ended);boss.Active=a.Value!=0;if(!boss.Active)boss.Proxy.jobs.StopAll();return true;}
    }
    [CampaignModule("rc.hediff","Action","Add/remove pawn hediff: Target pawn, Text HediffDef, Value 0 removes.")]
    public sealed class ActorHediff:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){var p=((CampaignController)host).ResolveTriggerPawn(a.Target);var def=DefDatabase<HediffDef>.GetNamed(a.Text);var h=p.health.hediffSet.GetFirstHediffOfDef(def);if(a.Value==0){if(h!=null)p.health.RemoveHediff(h);}else if(h==null)p.health.AddHediff(def);return true;}
    }
    [CampaignModule("rc.effect","Action","Native effect at a placement: Text lightning or skip.")]
    public sealed class SceneEffect:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;var cell=ResolveCell(c,a.Target);
            if(a.Text=="lightning")c.Map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(c.Map,cell));
            else{SoundDefOf.Psycast_Skip_Exit.PlayOneShot(new TargetInfo(cell,c.Map));FleckMaker.Static(cell,c.Map,FleckDefOf.PsycastAreaEffect,Math.Max(1,(float)a.Value));}return true;}
        public static IntVec3 ResolveCell(CampaignController c,string id){
            if(c.Objects.TryGetValue(id,out var thing)&&thing?.Spawned==true&&thing.Map==c.Map)return thing.Position;
            var p=c.MapSpec.Objects.Single(o=>o.Id==id);return new IntVec3(p.Position.X,0,p.Position.Z);
        }
    }
    [CampaignModule("rc.fire","Action","Scripted native weapon fire: Target shooter, Text target pawn, Value duration seconds.")]
    public sealed class SceneFire:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;var shooter=c.ResolveTriggerPawn(a.Target);var target=c.ResolveTriggerPawn(a.Text);if(shooter==null||shooter.Dead||shooter.Downed||!shooter.Spawned||shooter.Map!=c.Map||target==null||target.Dead||target.Downed||!target.Spawned||target.Map!=c.Map)return true;
            string key="fire:"+a.Id;c.State.Variables.TryGetValue(key,out var elapsed);c.State.Variables[key]=elapsed+delta;
            if(elapsed>=a.Value){shooter.jobs.StopAll();return true;}
            if(!shooter.stances.FullBodyBusy){var verb=shooter.TryGetAttackVerb(target,false);if(verb?.CanHitTarget(target)==true)verb.TryStartCastOn(target);}return false;}
    }
}
