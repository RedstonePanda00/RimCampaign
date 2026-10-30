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
 public sealed class CombatSupplyLoadout:DefModExtension {
  public ThingDef drug;public HediffDef drugEffect;public List<ThingDef> packs=new();
 }
 public sealed class CombatSupplyState:IExposable {
  public Pawn Pawn;public bool Issued,Prepared;
  public void ExposeData(){Scribe_References.Look(ref Pawn,"pawn");Scribe_Values.Look(ref Issued,"issued");Scribe_Values.Look(ref Prepared,"prepared");}
 }
 public sealed class SupplyUse:IExposable {
  public IntVec3 Cell;public Faction Faction;public string Kind;public int Tick;
  public void ExposeData(){Scribe_Values.Look(ref Cell,"cell");Scribe_References.Look(ref Faction,"faction");Scribe_Values.Look(ref Kind,"kind");Scribe_Values.Look(ref Tick,"tick");}
 }
 // Map opt-in: only explicitly configured PawnKinds participate. Item charges,
 // drug ingestion, firefoam and projectile interception stay with native code.
 public sealed class CombatSuppliesMapComponent:CustomMapComponent,ICampaignMapModule {
  bool enabled;int issuedCount;List<CombatSupplyState> people=new();List<SupplyUse> uses=new();
  public CombatSuppliesMapComponent(Map map):base(map){}
  public void Attach(CampaignController c,MapComponentSpec spec){enabled=spec.Enabled;}
  public void SetEnabled(bool value){enabled=value;}
  public void Detach(){enabled=false;}
  public override void MapRemoved(){Detach();people.Clear();uses.Clear();}
  public bool Manages(Pawn p)=>enabled&&p?.Map==map&&p.kindDef.GetModExtension<CombatSupplyLoadout>()!=null;
  public bool Prepare(Pawn p){
   if(!Manages(p)||p.Dead||p.Downed||p.InMentalState)return false;
   var loadout=p.kindDef.GetModExtension<CombatSupplyLoadout>();var state=people.FirstOrDefault(s=>s.Pawn==p);
   if(state==null){state=new CombatSupplyState{Pawn=p};people.Add(state);}
   if(!state.Issued){
    var packs=loadout.packs.Where(d=>d!=null).ToList();
    if(packs.Count>0&&p.apparel!=null&&!p.apparel.WornApparel.Any(a=>packs.Contains(a.def))){var pack=(Apparel)ThingMaker.MakeThing(packs[issuedCount%packs.Count]);p.apparel.Wear(pack);}
    issuedCount++;state.Issued=true;
   }
   if(state.Prepared)return true;
   if(loadout.drug==null||loadout.drugEffect!=null&&p.health.hediffSet.HasHediff(loadout.drugEffect)){state.Prepared=true;return true;}
   if(p.CurJob?.def==JobDefOf.Ingest&&p.CurJob.targetA.Thing?.def==loadout.drug)return false;
   var drug=p.inventory?.innerContainer.FirstOrDefault(t=>t.def==loadout.drug);
   if(drug==null){state.Prepared=true;return true;} // Exhausted inventory must not deadlock the task tree.
   var job=JobMaker.MakeJob(JobDefOf.Ingest,drug);job.count=1;job.playerForced=true;p.jobs.StartJob(job,JobCondition.InterruptForced);return false;
  }
  public bool Permit(Pawn p,string kind){
   if(!Manages(p))return true;
   if(p.Dead||p.Downed||!p.Awake()||p.InMentalState||!people.Any(s=>s.Pawn==p&&s.Prepared))return false;
   int now=Find.TickManager.TicksGame;
   if(uses.Any(u=>u.Kind==kind&&u.Faction==p.Faction&&now-u.Tick<180&&u.Cell.DistanceToSquared(p.Position)<=144))return false;
   if(kind=="foam")return GenRadial.RadialDistinctThingsAround(p.Position,map,4.9f,true).Any(t=>t is Fire||t.HasAttachment(ThingDefOf.Fire));
   if(kind=="shield"){
    if(!ModsConfig.RoyaltyActive)return false;
    if(map.listerThings.ThingsOfDef(ThingDefOf.BroadshieldProjector).Any(t=>t.TryGetComp<CompProjectileInterceptor>() is { Active:true } comp&&t.Position.DistanceTo(p.Position)<=comp.Props.radius))return false;
    return map.attackTargetsCache.GetPotentialTargetsFor(p).Any(t=>t.Thing.Spawned&&t.Thing.HostileTo(p)&&(!(t.Thing is Pawn enemy)||!enemy.Dead&&!enemy.Downed)&&p.Position.DistanceToSquared(t.Thing.Position)<=2025&&GenSight.LineOfSight(p.Position,t.Thing.Position,map)&&(now-p.LastAttackTargetTick<180||now-p.mindState.lastHarmTick<180||t.Thing is IAttackTargetSearcher attacker&&now-attacker.LastAttackTargetTick<180));
   }
   return true;
  }
  public void Used(Pawn p,string kind){if(Manages(p))uses.Add(new SupplyUse{Cell=p.Position,Faction=p.Faction,Kind=kind,Tick=Find.TickManager.TicksGame});}
  public override void MapComponentTick(){
   if(!enabled||Find.TickManager.TicksGame%30!=0)return;
   uses.RemoveAll(u=>Find.TickManager.TicksGame-u.Tick>=180);people.RemoveAll(s=>s.Pawn==null||s.Pawn.Destroyed||s.Pawn.Dead);
   foreach(var p in map.mapPawns.AllPawnsSpawned.OrderBy(p=>p.thingIDNumber).ToList()){
    if(!Manages(p)||!Prepare(p))continue;
    foreach(var pack in p.apparel.WornApparel.ToList()){
     var reload=pack.TryGetComp<CompApparelReloadable>();if(reload==null||!reload.CanBeUsed(out _))continue;
     if(pack is BroadshieldPack&&Permit(p,"shield"))Verb_DeployBroadshield.Deploy(reload);
     else if(pack.TryGetComp<CompFirefoamPack>()!=null&&Permit(p,"foam"))Verb_FirefoamPop.Pop(p,pack.TryGetComp<CompExplosive>(),reload);
    }
   }
  }
  public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref enabled,"enabled");Scribe_Values.Look(ref issuedCount,"issuedCount");Scribe_Collections.Look(ref people,"people",LookMode.Deep);Scribe_Collections.Look(ref uses,"uses",LookMode.Deep);if(Scribe.mode==LoadSaveMode.PostLoadInit){people??=new();uses??=new();}}
 }
 [CampaignModule("rc.task.prepareCombat","Action","Use the PawnKind combat loadout and ingest its carried drug before advancing.")]
 public sealed class PrepareCombat:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;var p=c.ResolveTriggerPawn(source);if(p?.Spawned!=true||p.Downed)return false;var supplies=c.Map.GetComponent<CombatSuppliesMapComponent>();return supplies==null||!supplies.Manages(p)||supplies.Prepare(p);}
 }
 // Native automatic triggers use the same coordination as scripted deployment.
 // Unregistered pawns and other maps retain their normal vanilla behaviour.
 [HarmonyPatch(typeof(Verb_DeployBroadshield),nameof(Verb_DeployBroadshield.Deploy))]
 static class CoordinatedShield {
  static bool Prefix(CompApparelReloadable comp,ref bool __result,ref Pawn __state){__state=comp?.Wearer;var coordinator=__state?.Map?.GetComponent<CombatSuppliesMapComponent>();if(coordinator==null||coordinator.Permit(__state,"shield"))return true;__result=false;return false;}
  static void Postfix(bool __result,Pawn __state){if(__result)__state?.Map?.GetComponent<CombatSuppliesMapComponent>()?.Used(__state,"shield");}
 }
 [HarmonyPatch(typeof(Verb_FirefoamPop),nameof(Verb_FirefoamPop.Pop))]
 static class CoordinatedFoam {
  static bool Prefix(Thing caster,ref bool __result){var p=caster as Pawn;var coordinator=p?.Map?.GetComponent<CombatSuppliesMapComponent>();if(coordinator==null||coordinator.Permit(p,"foam"))return true;__result=false;return false;}
  static void Postfix(bool __result,Thing caster){if(__result&&caster is Pawn p)p.Map?.GetComponent<CombatSuppliesMapComponent>()?.Used(p,"foam");}
 }
}
