using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimCampaign.Runtime
{
    public class CombatPatrolMemory {
        public string PawnId {get;set;} public string Route {get;set;}
        public int Index {get;set;} public int Direction {get;set;}=1;
        public int WaitUntil {get;set;} public float Radius {get;set;}=30;
    }
    public sealed class CombatPatrolMapComponent : CustomMapComponent, ICampaignMapModule {
        public bool Enabled;
        public List<PatrolRoute> Routes=new List<PatrolRoute>();
        public List<CombatPatrolMemory> Units=new List<CombatPatrolMemory>();
        Dictionary<string,int> ownedJobs=new Dictionary<string,int>();
        public CombatPatrolMapComponent(Map map):base(map){}
        public void Attach(CampaignController c,MapComponentSpec spec){
            Routes=Json.Copy(c.MapSpec.PatrolRoutes);Units.Clear();
            foreach(var binding in c.MapSpec.CombatPatrols){
                if(!c.Objects.TryGetValue(binding.Pawn,out var thing)||!(thing is Pawn pawn))throw new InvalidOperationException("Missing patrol pawn "+binding.Pawn);
                if(!Routes.Any(r=>r.Id==binding.Route&&r.Points.Count>0))throw new InvalidOperationException("Missing patrol route "+binding.Route);
                Units.Add(new CombatPatrolMemory{PawnId=pawn.ThingID,Route=binding.Route,Radius=binding.Radius});
            }
            Enabled=spec.Enabled;
        }
        public void SetEnabled(bool enabled){if(!enabled)Detach();Enabled=enabled;}
        public void Detach(){Enabled=false;foreach(var pawn in map.mapPawns.AllPawnsSpawned)if(ownedJobs.TryGetValue(pawn.ThingID,out var job)&&pawn.CurJob?.loadID==job){pawn.jobs.StopAll();pawn.pather.StopDead();}ownedJobs.Clear();}
        public override void ExposeData(){
            Scribe_Collections.Look(ref ownedJobs,"rcCombatOwnedJobs",LookMode.Value,LookMode.Value);
            Scribe_Values.Look(ref Enabled,"rcCombatPatrolEnabled");
            string routes=Json.Write(Routes),units=Json.Write(Units);ScribeJson.Look(ref routes,"rcCombatRoutes");ScribeJson.Look(ref units,"rcCombatUnits");
            if(Scribe.mode==LoadSaveMode.LoadingVars){Routes=Json.Read<List<PatrolRoute>>(routes);Units=Json.Read<List<CombatPatrolMemory>>(units);}
        }
        public override void MapComponentTick(){
            if(!Enabled||Find.TickManager.TicksGame%30!=0)return;
            var controller=CampaignController.ForMap(map);if(controller?.Map==map&&(controller.Freeze||controller.State.Phase!="Running"))return;
            foreach(var memory in Units){
                var pawn=map.mapPawns.AllPawnsSpawned.FirstOrDefault(p=>p.ThingID==memory.PawnId);
                if(pawn==null||pawn.Dead||pawn.Downed||pawn.InMentalState||pawn.stances.FullBodyBusy)continue;
                bool canFight=!pawn.WorkTagIsDisabled(WorkTags.Violent)&&
                    (pawn.equipment?.Primary?.def.IsRangedWeapon!=true||pawn.skills?.GetSkill(SkillDefOf.Shooting).TotallyDisabled!=true);
                var fight=canFight?new PatrolFight(memory.Radius).GetJob(pawn):null;
                if(fight!=null){
                    if(pawn.CurJob?.def==JobDefOf.AttackStatic||pawn.CurJob?.def==JobDefOf.AttackMelee||pawn.CurJob?.def==JobDefOf.Wait_Combat)continue;
                    if(pawn.CurJob?.def==fight.def&&pawn.CurJob.targetA==fight.targetA)continue;
                    Give(pawn,fight);continue;
                }
                var route=Routes.First(r=>r.Id==memory.Route);memory.Index=Math.Min(memory.Index,route.Points.Count-1);
                var point=route.Points[memory.Index];var at=new IntVec3(point.Position.X,0,point.Position.Z);
                if(pawn.Position.DistanceToSquared(at)<=2){
                    pawn.Rotation=new Rot4(point.Facing);
                    if(memory.WaitUntil==0)memory.WaitUntil=Find.TickManager.TicksGame+(point.Checkpoint?point.WaitTicks:1);
                    if(Find.TickManager.TicksGame<memory.WaitUntil){if(pawn.CurJob?.def!=JobDefOf.Wait_Combat)Give(pawn,JobMaker.MakeJob(JobDefOf.Wait_Combat,60));continue;}
                    memory.WaitUntil=0;
                    if(route.PingPong&&route.Points.Count>1&&(memory.Index+memory.Direction<0||memory.Index+memory.Direction>=route.Points.Count))memory.Direction=-memory.Direction;
                    memory.Index=(memory.Index+memory.Direction+route.Points.Count)%route.Points.Count;
                    point=route.Points[memory.Index];at=new IntVec3(point.Position.X,0,point.Position.Z);
                }
                if(pawn.CurJob?.def==JobDefOf.Goto&&pawn.CurJob.targetA.Cell==at)continue;
                if(!pawn.CanReach(at,PathEndMode.OnCell,Danger.Deadly))continue;
                var move=JobMaker.MakeJob(JobDefOf.Goto,at);move.locomotionUrgency=LocomotionUrgency.Walk;Give(pawn,move);
            }
        }
        void Give(Pawn pawn,Job job){ownedJobs[pawn.ThingID]=job.loadID;pawn.jobs.StartJob(job,JobCondition.InterruptForced);}
        sealed class PatrolFight : JobGiver_AIFightEnemies {
            public PatrolFight(float radius){targetAcquireRadius=radius;targetKeepRadius=radius+8;chaseTarget=true;allowTurrets=true;}
            public Job GetJob(Pawn pawn)=>TryGiveJob(pawn);
        }
    }
}
