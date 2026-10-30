using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimCampaign.Runtime {
    // Opt-in autonomous guide. The guide waits for its follower, and never wins the map alone.
    public sealed class EscortMapComponent:CustomMapComponent,ICampaignMapModule {
        public bool Enabled,Running,Arrived;
        public string Guide="",Follower="";
        public List<Cell> Waypoints=new List<Cell>();
        public int Index;
        public float WaitDistance=16;
        CampaignController Controller {get{var c=CampaignController.ForMap(map);return c?.Active==true&&c.Map==map?c:null;}}
        public Pawn GuidePawn=>Controller?.ResolveTriggerPawn(Guide);
        public EscortMapComponent(Map map):base(map){}
        public void Attach(CampaignController c,MapComponentSpec spec){Enabled=spec.Enabled;}
        public void SetEnabled(bool value){if(!value)Detach();Enabled=value;}
        public void Detach(){Running=false;GuidePawn?.jobs?.StopAll();Enabled=false;}
        public void Begin(string guide,string follower,List<Cell> route){Guide=guide;Follower=follower;Waypoints=route;Index=0;Running=true;Arrived=false;}
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref Enabled,"enabled");Scribe_Values.Look(ref Running,"running");Scribe_Values.Look(ref Arrived,"arrived");Scribe_Values.Look(ref Guide,"guide","");Scribe_Values.Look(ref Follower,"follower","");Scribe_Values.Look(ref Index,"waypoint");Scribe_Values.Look(ref WaitDistance,"waitDistance",16);string json=Json.Write(Waypoints);ScribeJson.Look(ref json,"waypoints");if(Scribe.mode==LoadSaveMode.LoadingVars)Waypoints=Json.Read<List<Cell>>(json);}
        public override void MapComponentTick(){
            if(!Enabled||(!Running&&!Arrived)||Find.TickManager.TicksGame%30!=0)return;
            var c=Controller;if(c==null||c.Presentation||c.State.Phase!="Running")return;
            var guide=GuidePawn;var follower=c.ResolveTriggerPawn(Follower);if(guide?.Spawned!=true||follower?.Spawned!=true||guide.Map!=map||follower.Map!=map||guide.Dead||guide.Downed)return;
            foreach(var need in guide.needs.AllNeeds)need.CurLevelPercentage=1;
            if(Arrived){
                if(Waypoints.Count==0)return;
                var end=Waypoints[Waypoints.Count-1];var destination=new IntVec3(end.X,0,end.Z);
                if(guide.Position.DistanceTo(destination)<=1){if(guide.pather.Moving)guide.jobs.StopAll();return;}
                if(guide.CurJob?.def!=JobDefOf.Goto||guide.CurJob.targetA.Cell!=destination)
                    if(guide.CanReach(destination,PathEndMode.OnCell,Danger.Deadly))guide.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto,destination),JobCondition.InterruptForced);
                return;
            }
            while(Index<Waypoints.Count&&guide.Position.DistanceTo(new IntVec3(Waypoints[Index].X,0,Waypoints[Index].Z))<=(Index==Waypoints.Count-1?1:3))Index++;
            if(Index>=Waypoints.Count){Running=false;Arrived=true;guide.jobs.StopAll();c.Signal("EscortArrived",Guide);return;}
            var cell=new IntVec3(Waypoints[Index].X,0,Waypoints[Index].Z);
            if(guide.Position.DistanceTo(follower.Position)>WaitDistance&&follower.Position.DistanceTo(cell)>guide.Position.DistanceTo(cell)){if(guide.pather.Moving)guide.jobs.StopAll();return;}
            if(!cell.Standable(map))cell=CellFinder.StandableCellNear(cell,map,4);
            if(guide.CurJob?.def==JobDefOf.Goto&&guide.CurJob.targetA.Cell==cell)return;
            if(guide.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))guide.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto,cell),JobCondition.InterruptForced);
        }
    }
    [CampaignModule("rc.escort","Action","Start autonomous guide: Target guide, Text follower, Steps targets are waypoint IDs.")]
    public sealed class StartEscort:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;c.Map.GetComponent<EscortMapComponent>().Begin(a.Target,a.Text,a.Steps.Select(s=>c.MapSpec.Objects.Single(p=>p.Id==s.Target).Position).ToList());return true;}
    }
    [CampaignModule("rc.escortArrived","Condition","Whether the attached guide has reached its final waypoint.")]
    public sealed class EscortArrived:IConditionModule {public bool Evaluate(Condition condition,IHost host)=>((CampaignController)host).Map.GetComponent<EscortMapComponent>()?.Arrived==true;}
    [CampaignModule("rc.near","Condition","Key is pawn|marker; Value is radius.")]
    public sealed class ActorNear:IConditionModule {public bool Evaluate(Condition condition,IHost host){var c=(CampaignController)host;var ids=condition.Key.Split('|');var pawn=c.ResolveTriggerPawn(ids[0]);var at=c.MapSpec.Objects.Single(o=>o.Id==ids[1]).Position;return pawn?.Spawned==true&&pawn.Position.DistanceTo(new IntVec3(at.X,0,at.Z))<=condition.Value;}}
    [HarmonyPatch(typeof(Pawn_JobTracker),"DetermineNextJob")]
    static class GuideThinking {
        static bool Prefix(Pawn ___pawn,ref ThinkTreeDef __0,ref ThinkResult __result){var module=___pawn.Map?.GetComponent<EscortMapComponent>();if(module?.Enabled!=true||module.GuidePawn!=___pawn)return true;__0=null;__result=ThinkResult.NoJob;return false;}
    }
}
