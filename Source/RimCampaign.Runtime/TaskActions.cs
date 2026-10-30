using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
namespace RimCampaign.Runtime {
 // Runtime map specifications are immutable. Build references once per loaded spec,
 // rather than walking every terrain/wall placement on each task tick.
 static class TaskCatalog {
  static readonly ConditionalWeakTable<MapSpec,TaskCatalogIndex> cache=new ConditionalWeakTable<MapSpec,TaskCatalogIndex>();
  public static TaskCatalogIndex For(CampaignController c)=>cache.GetValue(c.MapSpec,m=>new TaskCatalogIndex(m));
 }
 sealed class TaskCatalogIndex {
  readonly Dictionary<string,Cell> positions;
  readonly Dictionary<string,List<Placement>> groups;
  readonly Dictionary<string,string> roles;
  public TaskCatalogIndex(MapSpec map){positions=map.Objects.ToDictionary(o=>o.Id,o=>o.Position);foreach(var r in map.Rules)positions[r.Id]=r.Position;groups=map.Objects.Where(o=>!string.IsNullOrEmpty(o.Group)).GroupBy(o=>o.Group).ToDictionary(g=>g.Key,g=>g.ToList());roles=map.Objects.Where(o=>o.Kind=="Pawn").ToDictionary(o=>o.Id,o=>o.Role);}
  public string Role(string id)=>roles.TryGetValue(id,out var role)?role:null;
  public IEnumerable<Placement> Group(string id)=>groups.TryGetValue(id,out var list)?list:Enumerable.Empty<Placement>();
  public IntVec3 Cell(string id)=>positions.TryGetValue(id,out var p)?new IntVec3(p.X,0,p.Z):IntVec3.Invalid;
 }
 static class TaskNavigation {
  sealed class ScanClock { public int LastTick=-30; }
  static readonly ConditionalWeakTable<Pawn,ScanClock> clocks=new ConditionalWeakTable<Pawn,ScanClock>();
  public static bool CombatScanReady(Pawn pawn){var clock=clocks.GetValue(pawn,_=>new ScanClock());int now=Find.TickManager.TicksGame;if(now>=clock.LastTick&&now-clock.LastTick<30)return false;clock.LastTick=now;return true;}
  public static void ReleaseDuty(Pawn pawn){pawn.GetLord()?.Notify_PawnLost(pawn,PawnLostCondition.ForcedToJoinOtherLord);}
  public static bool DefendWhileTravelling(Pawn pawn){
   bool Threat(Thing t)=>t?.Spawned==true&&t.Map==pawn.Map&&t.HostileTo(pawn)&&(!(t is Pawn q)||!q.Dead&&!q.Downed)&&pawn.Position.DistanceToSquared(t.Position)<=900&&pawn.TryGetAttackVerb(t,false)?.CanHitTarget(t)==true;
   if((pawn.CurJob?.def==JobDefOf.AttackStatic||pawn.CurJob?.def==JobDefOf.AttackMelee)&&Threat(pawn.CurJob.targetA.Thing))return true;
   if(!CombatScanReady(pawn))return false;
   var target=pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn).Select(t=>t.Thing).Where(Threat).OrderBy(t=>t.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
   if(target==null)return false;
   var verb=pawn.TryGetAttackVerb(target,false);var job=JobMaker.MakeJob(verb.IsMeleeAttack?JobDefOf.AttackMelee:JobDefOf.AttackStatic,target);
   job.maxNumStaticAttacks=1;job.playerForced=true;pawn.jobs.StartJob(job,JobCondition.InterruptForced);return true;
  }
  public static void Approach(Pawn pawn,Thing target){
   if(pawn.CurJob?.def==JobDefOf.Goto&&pawn.CurJob.targetB.Thing==target)return;
   foreach(var cell in GenAdj.CellsAdjacent8Way(target).OrderBy(c=>c.DistanceToSquared(pawn.Position))){
    if(!cell.InBounds(pawn.Map)||!cell.Standable(pawn.Map)||!GenSight.LineOfSight(cell,target.Position,pawn.Map)||!pawn.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))continue;
    var job=JobMaker.MakeJob(JobDefOf.Goto,cell,target);job.playerForced=true;pawn.jobs.StartJob(job,JobCondition.InterruptForced);return;
   }
  }
 }
 public partial class CampaignController:ITaskActorHost {
  public bool TaskActorAvailable(string actor){var p=ResolveTriggerPawn(actor);return p!=null&&!p.Dead&&!p.Destroyed;}
 }
 // Task state uses existing serialized fibers and variables. Coordinates remain in map placements.
 [CampaignModule("rc.task.move","Action","Bound actor walks to Target map marker; Value is arrival radius (tiles).")]
 public sealed class TaskMove:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){
   var c=(CampaignController)host;var p=c.ResolveTriggerPawn(source);if(p==null||p.Dead||p.Downed||!p.Spawned||p.Map!=c.Map)return false;
   if(a.Text=="Combat"&&TaskNavigation.DefendWhileTravelling(p))return false;
   var cell=TaskCatalog.For(c).Cell(a.Target);if(!cell.IsValid)return false;if(p.Position.DistanceTo(cell)<=Math.Max(0,a.Value))return true;
   if(p.CurJob?.def!=JobDefOf.Goto||p.CurJob.targetA.Cell!=cell){if(!p.CanReach(cell,PathEndMode.OnCell,Danger.Deadly))return false;var job=JobMaker.MakeJob(JobDefOf.Goto,cell);job.playerForced=true;p.jobs.StartJob(job,JobCondition.InterruptForced);}return false;
  }
 }
 [CampaignModule("rc.task.operate","Action","Bound actor touches Target building and works for Value seconds. Interruption resets progress.")]
 public sealed class TaskOperate:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){
   var c=(CampaignController)host;var p=c.ResolveTriggerPawn(source);string key="task-work:"+a.Id;
   if(p==null||p.Dead||p.Downed||!p.Spawned||p.Map!=c.Map)return false;
   if(a.Text=="Combat"&&TaskNavigation.DefendWhileTravelling(p)){c.State.Variables[key]=0;return false;}
   if(!c.Objects.TryGetValue(a.Target,out var target)||!target.Spawned)return false;
   if(!p.CanReachImmediate(target,PathEndMode.Touch)){
    c.State.Variables[key]=0;TaskNavigation.Approach(p,target);return false;
   }
   if(p.CurJob?.def!=JobDefOf.Wait||p.CurJob.targetA.Thing!=target){c.State.Variables[key]=0;var job=JobMaker.MakeJob(JobDefOf.Wait,target);job.expiryInterval=Math.Max(60,(int)(a.Value*60)+60);job.playerForced=true;p.jobs.StartJob(job,JobCondition.InterruptForced);return false;}
   p.rotationTracker.FaceTarget(target);c.State.Variables.TryGetValue(key,out var elapsed);elapsed+=delta;c.State.Variables[key]=elapsed;
   if(elapsed<Math.Max(.1,a.Value))return false;c.State.Variables.Remove(key);p.jobs.StopAll();return true;
  }
 }
 [CampaignModule("rc.task.hold","Action","Hold Target marker within Value tiles until When; Text=Combat permits local self-defense.")]
 public sealed class TaskHold:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){
   var c=(CampaignController)host;var pawn=c.ResolveTriggerPawn(source);if(pawn?.Spawned!=true||pawn.Downed)return false;
   if(!new TaskMove().Execute(a,host,source,delta))return false;
   if(c.TestCondition(a.When))return true;
   if(pawn.CurJob?.def!=JobDefOf.Wait_Combat){var job=JobMaker.MakeJob(JobDefOf.Wait_Combat);job.expiryInterval=120;job.playerForced=true;pawn.jobs.StartJob(job,JobCondition.InterruptForced);}return false;
  }
 }
 [CampaignModule("rc.targetsCleared","Condition","Key is a placement group; true when all its spawned defenders are dead, downed or destroyed.")]
 public sealed class TaskTargetsCleared:IConditionModule {
  public bool Evaluate(Condition a,IHost host){var c=(CampaignController)host;var group=TaskCatalog.For(c).Group(a.Key);return group.Any()&&group.All(o=>c.Objects.TryGetValue(o.Id,out var t)&&t!=null?(t.Destroyed||t is Pawn p&&(p.Dead||p.Downed)):c.State.Variables.ContainsKey("destroyed-placement:"+o.Id));}
 }
 [CampaignModule("rc.task.engage","Action","Attack Target group immediately; advance only when defenders are defeated AND When is true. Reacquire replenished defenders while waiting.")]
 public sealed class TaskEngage:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){
   var c=(CampaignController)host;var p=c.ResolveTriggerPawn(source);if(p?.Spawned!=true||p.Downed)return false;
   if(!TaskNavigation.CombatScanReady(p))return false;
   TaskNavigation.ReleaseDuty(p);
   var targets=TaskCatalog.For(c).Group(a.Target).Select(o=>c.Objects.TryGetValue(o.Id,out var t)?t:null).Where(t=>t?.Spawned==true&&(!(t is Pawn pawn)||!pawn.Dead&&!pawn.Downed)).ToList();
   if(targets.Count==0)return c.TestCondition(a.When);
   var current=p.CurJob?.targetA.Thing;
   if((p.CurJob?.def==JobDefOf.AttackStatic||p.CurJob?.def==JobDefOf.AttackMelee)&&current!=null&&targets.Contains(current)&&p.TryGetAttackVerb(current,false)?.CanHitTarget(current)==true)return false;
   var target=targets.MinBy(t=>p.Position.DistanceToSquared(t.Position));var verb=p.TryGetAttackVerb(target,false);
   if(verb?.IsMeleeAttack==true){if(p.CurJob?.def!=JobDefOf.AttackMelee||p.CurJob.targetA.Thing!=target)p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.AttackMelee,target),JobCondition.InterruptForced);return false;}
   if(verb?.CanHitTarget(target)==true){if(p.CurJob?.def!=JobDefOf.AttackStatic||p.CurJob.targetA.Thing!=target){var job=JobMaker.MakeJob(JobDefOf.AttackStatic,target);job.maxNumStaticAttacks=4;p.jobs.StartJob(job,JobCondition.InterruptForced);}}
   else if(p.CurJob?.def!=JobDefOf.Goto||p.CurJob.targetB.Thing!=target){
    if(verb!=null&&CastPositionFinder.TryFindCastPosition(new CastPositionRequest{caster=p,target=target,verb=verb,maxRangeFromTarget=verb.verbProps.range,wantCoverFromTarget=true},out var firingCell)){
     var move=JobMaker.MakeJob(JobDefOf.Goto,firingCell,target);p.jobs.StartJob(move,JobCondition.InterruptForced);
    }else TaskNavigation.Approach(p,target);
   }return false;
  }
 }
 [CampaignModule("rc.task.signal","Action","Emit Target event with Text identifier after the preceding task completes.")]
 public sealed class TaskSignal:IActionModule {public bool Execute(Act a,IHost host,string source,double delta){((CampaignController)host).Signal(a.Target,a.Text,source);return true;}}
 [CampaignModule("rc.reinforce","Action","Refill up to Value empty slots in Target placement group, starting Text task tree for each arrival. Slot count caps living units.")]
 public sealed class TaskReinforce:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;int count=0;
   int index=0;foreach(var slot in TaskCatalog.For(c).Group(a.Target).Where(o=>o.Kind=="PawnKind")){
    int formation=index++;
    if(count>=Math.Max(1,(int)a.Value))break;if(c.Objects.TryGetValue(slot.Id,out var prior)&&prior is Pawn priorPawn&&!priorPawn.Dead&&!priorPawn.Destroyed)continue;
    var placement=slot;
    if(!string.IsNullOrEmpty(a.Origin)){
     var origin=TaskCatalog.For(c).Cell(a.Origin);if(!origin.IsValid)throw new InvalidOperationException("Missing reinforcement origin: "+a.Origin);
     var desired=origin+new IntVec3(formation%6*2,0,formation/6*2);
     var cell=CellFinder.StandableCellNear(desired,c.Map,8);placement=Json.Copy(slot);placement.Position=new Cell{X=cell.x,Z=cell.z};
    }
    Pawn pawn;
    if(a.Arrival=="Pods"){
     var faction=Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(c.MapSpec,slot.Faction);
     pawn=Profiles.Generate(slot.Def,faction);c.Objects[slot.Id]=pawn;
     TaskDropPods.Drop(new IntVec3(placement.Position.X,0,placement.Position.Z),c.Map,pawn,faction);
    }else pawn=(Pawn)c.Place(placement,c.Map);
    c.RegisterTaskPawn(pawn);
    // Bind the instance ID, so a replacement cannot inherit a dead soldier's pending tasks.
    if(!string.IsNullOrEmpty(a.Text))c.QueueStory(new[]{new Act{Kind="Group",Target=a.Text}},pawn.ThingID);count++;
   }return true;
  }
 }
 [CampaignModule("rc.defendGroup","Action","Assign unassigned living members of Target placement group to a shared native defend-point duty at Text marker.")]
 public sealed class DefendGroup:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){
   var c=(CampaignController)host;var pawns=TaskCatalog.For(c).Group(a.Target).Select(o=>c.Objects.TryGetValue(o.Id,out var t)?t as Pawn:null).Where(p=>p?.Spawned==true&&!p.Dead).ToList();
   if(pawns.Count==0)return true;var point=TaskCatalog.For(c).Cell(a.Text);if(!point.IsValid)throw new InvalidOperationException("Missing defense point: "+a.Text);
   var lord=pawns.Select(p=>p.GetLord()).FirstOrDefault(l=>l?.LordJob is LordJob_DefendPoint);
   var newcomers=pawns.Where(p=>p.GetLord()==null).ToList();if(newcomers.Count==0)return true;
   if(lord==null)LordMaker.MakeNewLord(newcomers[0].Faction,new LordJob_DefendPoint(point,(float)Math.Min(10,Math.Max(4,a.Value*.5)),(float)Math.Max(5,a.Value),false,false),c.Map,newcomers);else foreach(var p in newcomers)lord.AddPawn(p);
   return true;
  }
 }
}

