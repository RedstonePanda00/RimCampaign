using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimCampaign.Runtime {
 public class BossArenaRule {
  public string Actor{get;set;} public string Anchor{get;set;} public string Region{get;set;}
  public string ActivationVariable{get;set;} public string TargetFaction{get;set;}
 }
 // Optional, map-owned encounter limits. Reusable bosses keep their own health and plugins.
 public sealed class BossArenaMapComponent:CustomMapComponent,ICampaignMapModule {
  bool enabled;List<BossArenaRule> rules=new List<BossArenaRule>();
  CampaignController C=>CampaignController.ForMap(map);
  public BossArenaMapComponent(Map map):base(map){}
  public void Attach(CampaignController c,MapComponentSpec spec){
   rules=spec.Settings.TryGetValue("rules",out var json)?Json.Read<List<BossArenaRule>>(json):new List<BossArenaRule>();
   foreach(var r in rules)if(!c.MapSpec.Regions.Any(z=>z.Id==r.Region)||!c.MapSpec.Objects.Any(p=>p.Id==r.Anchor)||!c.MapSpec.Objects.Any(p=>p.Id==r.Actor))throw new InvalidOperationException("Boss arena requires existing actor, anchor and region.");
   enabled=spec.Enabled;
  }
  public void SetEnabled(bool value){enabled=value;}
  public void Detach(){enabled=false;}
  public BossArenaRule RuleFor(Pawn p)=>enabled&&C?.Map==map?rules.FirstOrDefault(r=>C.Objects.TryGetValue(r.Actor,out var t)&&t==p):null;
  public bool Activated(BossArenaRule r)=>string.IsNullOrEmpty(r.ActivationVariable)||C.State.Variables.GetValueOrDefault(r.ActivationVariable)>=1;
  public IntVec3 Anchor(BossArenaRule r){var at=C.MapSpec.Objects.First(p=>p.Id==r.Anchor).Position;return new IntVec3(at.X,0,at.Z);}
  public CellRect Bounds(BossArenaRule r){var z=C.MapSpec.Regions.First(v=>v.Id==r.Region);return new CellRect(z.Min.X,z.Min.Z,z.Max.X-z.Min.X+1,z.Max.Z-z.Min.Z+1);}
  public bool AllowsTarget(BossArenaRule r,Thing target)=>Activated(r)&&target?.Spawned==true&&target.Map==map&&Bounds(r).Contains(target.Position)&&
   (string.IsNullOrEmpty(r.TargetFaction)||target.Faction==Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(C.MapSpec,r.TargetFaction));
  public bool AllowsStep(BossArenaRule r,Pawn p,IntVec3 next){
   var bounds=Bounds(r);if(bounds.Contains(next))return true;
   // Old saves with an actor already outside can walk back, but never farther out.
   int Distance(IntVec3 v)=>Math.Max(bounds.minX-v.x,Math.Max(0,v.x-bounds.maxX))+Math.Max(bounds.minZ-v.z,Math.Max(0,v.z-bounds.maxZ));
   return !bounds.Contains(p.Position)&&Distance(next)<Distance(p.Position);
  }
  public override void MapComponentTick(){
   if(!enabled||C?.Map!=map||Find.TickManager.TicksGame%30!=0)return;
   foreach(var r in rules){
    if(!C.Objects.TryGetValue(r.Actor,out var thing)||thing is not Pawn p||!p.Spawned||p.Dead||p.Downed)continue;
    bool engage=Activated(r)&&map.attackTargetsCache.GetPotentialTargetsFor(p).Any(t=>AllowsTarget(r,t.Thing)&&t.Thing.HostileTo(p)&&(!(t.Thing is Pawn q)||!q.Dead&&!q.Downed));
    if(engage)continue;
    TaskNavigation.ReleaseDuty(p);var anchor=Anchor(r);
    if(p.Position!=anchor){if(p.CurJob?.def==JobDefOf.Goto&&p.CurJob.targetA.Cell==anchor)continue;var go=JobMaker.MakeJob(JobDefOf.Goto,anchor);go.playerForced=true;p.jobs.StartJob(go,JobCondition.InterruptForced);}
    else if(p.CurJob?.def!=JobDefOf.Wait){var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=120;wait.playerForced=true;p.jobs.StartJob(wait,JobCondition.InterruptForced);}
   }
  }
  public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref enabled,"rcArenaEnabled");string json=Json.Write(rules);ScribeJson.Look(ref json,"rcArenaRules");if(Scribe.mode==LoadSaveMode.LoadingVars)rules=string.IsNullOrEmpty(json)?new List<BossArenaRule>():Json.Read<List<BossArenaRule>>(json);}
 }
 [StaticConstructorOnStartup]
 static class BossArenaPermissions {
  static BossArenaPermissions(){BossMapComponent.BehaviorPermission+=p=>{var arena=p.Map?.GetComponent<BossArenaMapComponent>();var rule=arena?.RuleFor(p);return rule==null||arena.Activated(rule);};
   BossMapComponent.TargetPermission+=(p,t)=>{var arena=p.Map?.GetComponent<BossArenaMapComponent>();var rule=arena?.RuleFor(p);return rule==null||arena.AllowsTarget(rule,t);};}
 }
 [HarmonyPatch(typeof(Pawn_PathFollower),"TryEnterNextPathCell")]
 static class BossArenaMovement {
  static bool Prefix(Pawn_PathFollower __instance,Pawn ___pawn){var arena=___pawn.Map?.GetComponent<BossArenaMapComponent>();var rule=arena?.RuleFor(___pawn);if(rule==null||arena.AllowsStep(rule,___pawn,__instance.nextCell))return true;__instance.StopDead();return false;}
 }
 [HarmonyPatch(typeof(Pawn_JobTracker),nameof(Pawn_JobTracker.StartJob))]
 static class BossArenaJobs {
  static bool Prefix(Pawn ___pawn,Job newJob){
   var arena=___pawn.Map?.GetComponent<BossArenaMapComponent>();var rule=arena?.RuleFor(___pawn);if(rule==null||newJob==null||___pawn.Downed||___pawn.Dead)return true;
   if(newJob.def==JobDefOf.Wait)return true;
   if(!arena.Activated(rule))return newJob.def==JobDefOf.Goto&&newJob.targetA.Cell==arena.Anchor(rule);
   if(newJob.def==JobDefOf.AttackMelee||newJob.def==JobDefOf.AttackStatic)return arena.AllowsTarget(rule,newJob.targetA.Thing);
   return newJob.def==JobDefOf.Goto&&arena.AllowsStep(rule,___pawn,newJob.targetA.Cell);
  }
 }
}
