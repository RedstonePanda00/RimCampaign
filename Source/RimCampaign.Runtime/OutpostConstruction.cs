using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimCampaign.Runtime {
 // Vanilla general refueling excludes non-colonists. This work giver is enabled only
 // for this native construction Lord and visits its authored cells, not all map buildings.
 public sealed class WorkGiver_OutpostRefuel:WorkGiver_Refuel {
  public override bool ShouldSkip(Pawn pawn,bool forced=false)=>pawn.GetLord()?.LordJob is not LordJob_CampaignOutpost;
  public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)=>pawn.GetLord()?.LordJob is LordJob_CampaignOutpost job
   ?job.Sites.Select(cell=>cell.GetEdifice(pawn.Map)).Where(t=>t!=null&&t.TryGetComp<CompRefuelable>()!=null):Enumerable.Empty<Thing>();
 }
 [CampaignModule("rc.buildOutpost","Action","Target engineers build Text placement group at Origin marker using native blueprints and air-dropped materials, then defend it.")]
 public sealed class BuildOutpost:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){
   var c=(CampaignController)host;
   var engineers=TaskCatalog.For(c).Group(a.Target).Select(o=>c.Objects.TryGetValue(o.Id,out var t)?t as Pawn:null).Where(p=>p!=null&&!p.Dead&&!p.Destroyed).ToList();
   if(engineers.Count==0)return true;
   if(engineers.Any(p=>!p.Spawned))return false;
   var preparation=c.Map.GetComponent<CombatSuppliesMapComponent>();
   if(preparation!=null){bool ready=true;foreach(var pawn in engineers)if(preparation.Manages(pawn)&&!preparation.Prepare(pawn))ready=false;if(!ready)return false;}
   var point=TaskCatalog.For(c).Cell(a.Origin);var faction=engineers[0].Faction;
   var sites=new List<IntVec3>();var materials=new Dictionary<ThingDef,int>();
   foreach(var placement in TaskCatalog.For(c).Group(a.Text).Where(o=>o.Kind=="Thing")){
    var def=DefDatabase<ThingDef>.GetNamed(placement.Def.Name);
    var pos=new IntVec3(placement.Position.X,0,placement.Position.Z);var rot=new Rot4(placement.Rotation);
    var stuff=PlacementStorage.StuffFor(def,placement.Stuff);
    if(!GenConstruct.CanPlaceBlueprintAt(def,pos,rot,c.Map,false,null,null,stuff).Accepted)throw new InvalidOperationException("Cannot place outpost blueprint: "+placement.Name);
    var blueprint=GenConstruct.PlaceBlueprintForBuild(def,pos,c.Map,rot,faction,stuff);sites.Add(pos);
    foreach(var cost in blueprint.TotalMaterialCost()){materials.TryGetValue(cost.thingDef,out int count);materials[cost.thingDef]=count+cost.count;}
   }
   // Spare construction materials cover native construction failures; fuel is delivered, not synthesized.
   var supplies=new List<Thing>();
   materials[ThingDefOf.Chemfuel]=materials.GetValueOrDefault(ThingDefOf.Chemfuel)+150;
   materials[ThingDefOf.MealSurvivalPack]=materials.GetValueOrDefault(ThingDefOf.MealSurvivalPack)+24;
   foreach(var pair in materials){int remaining=(int)Math.Ceiling(pair.Value*1.2);while(remaining>0){var item=ThingMaker.MakeThing(pair.Key);item.stackCount=Math.Min(remaining,pair.Key.stackLimit);remaining-=item.stackCount;supplies.Add(item);}}
   foreach(var supply in supplies)TaskDropPods.Drop(point,c.Map,supply,faction);
   foreach(var pawn in engineers)TaskNavigation.ReleaseDuty(pawn);
   LordMaker.MakeNewLord(faction,new LordJob_CampaignOutpost(point,sites),c.Map,engineers);
   return true;
  }
 }
 // A small native Lord owns build/defend duties. No campaign-global tick or instant construction.
 public sealed class LordJob_CampaignOutpost:LordJob {
  public IntVec3 Center;public List<IntVec3> Sites=new List<IntVec3>();
  public LordJob_CampaignOutpost(){}
  public LordJob_CampaignOutpost(IntVec3 center,List<IntVec3> sites){Center=center;Sites=sites;}
  public override StateGraph CreateGraph(){var graph=new StateGraph();graph.AddToil(new LordToil_CampaignOutpost());return graph;}
  public override void ExposeData(){Scribe_Values.Look(ref Center,"outpostCenter");Scribe_Collections.Look(ref Sites,"outpostSites",LookMode.Value);}
 }
 public sealed class LordToil_CampaignOutpost:LordToil {
  LordJob_CampaignOutpost Job=>(LordJob_CampaignOutpost)lord.LordJob;
  public override IntVec3 FlagLoc=>Job.Center;
  public override void Init(){base.Init();UpdateAllDuties();}
  public override void UpdateAllDuties(){
   foreach(var pawn in lord.ownedPawns){
    pawn.mindState.duty=new PawnDuty(DutyDefOf.Build,Job.Center){radius=16};
    pawn.workSettings.EnableAndInitialize();
    foreach(var work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
     if(work==WorkTypeDefOf.Construction||work==WorkTypeDefOf.Hauling||work==WorkTypeDefOf.Firefighter)pawn.workSettings.SetPriority(work,work==WorkTypeDefOf.Construction?1:2);else pawn.workSettings.Disable(work);
   }
  }
 }
}
