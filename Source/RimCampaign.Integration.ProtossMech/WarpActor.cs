using System;
using System.Linq;
using ProtossMech;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimCampaign.Integration.ProtossMech {
    [CampaignModule("rc.pmcWarpActor","Action","Warp an existing campaign actor; Target is its placement ID, Value is seconds.")]
    public sealed class WarpActor : IActionModule {
        public bool Execute(Act action,IHost host,string source,double delta){
            var c=(CampaignController)host;var placement=c.MapSpec.Objects.Single(p=>p.Id==action.Target);
            if(placement.Kind!="Pawn")throw new InvalidOperationException("Actor warp requires a campaign pawn placement.");
            Thing actor=c.Actors[placement.Role];
            string pending="warp:"+action.Id;
            if(actor?.Spawned==true){c.Objects.Remove(pending);return true;}
            if(c.Objects.TryGetValue(pending,out var previous)){
                if(previous==null||previous.Destroyed)throw new InvalidOperationException("Actor warp interrupted: "+action.Target);
                return false;
            }
            var beacon=(ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RC_PMCActorBeacon"));
            beacon.GetComp<CompWarpActorBinding>().Target=placement.Id;
            // A null payload makes PMC run its existing visual/countdown without generating a replacement pawn.
            beacon.GetComp<CompWarpIn>().Setup(null,Math.Max(1,(int)Math.Round((action.Value>0?action.Value:3)*60)));
            GenSpawn.Spawn(beacon,new IntVec3(placement.Position.X,0,placement.Position.Z),c.Map);c.Objects[pending]=beacon;
            return false;
        }
    }
    [CampaignModule("rc.pmcWarpBuilding","Action","Gift a player building with PMC's construction warp; Target placement, optional Value duration in seconds.")]
    public sealed class WarpBuilding:IActionModule {
        public bool Execute(Act action,IHost host,string source,double delta){
            var c=(CampaignController)host;var p=c.MapSpec.Objects.Single(o=>o.Id==action.Target);
            var def=DefDatabase<ThingDef>.GetNamed(p.Def.Name);var at=new IntVec3(p.Position.X,0,p.Position.Z);
            if(p.Kind!="Thing"||def.category!=ThingCategory.Building||p.Faction!="Player")throw new InvalidOperationException("Building gift warp requires a player building placement.");
            string pending="building-warp:"+action.Id;
            var building=at.GetThingList(c.Map).FirstOrDefault(t=>t.def==def&&t.Position==at);
            if(building!=null){c.Objects[p.Id]=building;c.Objects.Remove(pending);c.Map.GetComponent<SceneInteractionMapComponent>()?.RefreshBindings();return true;}
            if(c.Objects.TryGetValue(pending,out var previous)){
                if(previous?.Spawned!=true)throw new InvalidOperationException("Building warp interrupted: "+p.Id);
                return false;
            }
            var beacon=ProbeWarpConstructionUtility.SpawnGiftBuilding(def,at,c.Map,new Rot4(p.Rotation),null);
            if(action.Value>0)beacon.TryGetComp<CompWarpConstructionBeacon>().SetupForBuilding(def,Faction.OfPlayer,null,Math.Max(1,(int)Math.Round(action.Value*60)));
            c.Objects[pending]=beacon;return false;
        }
    }
    public sealed class CompProperties_WarpActorBinding : CompProperties {
        public CompProperties_WarpActorBinding(){compClass=typeof(CompWarpActorBinding);}
    }
    public sealed class CompWarpActorBinding : ThingComp {
        public string Target;
        public string CompletionKey,Weapon;
        public bool DraftOnArrival;
        public override void PostExposeData(){Scribe_Values.Look(ref Target,"campaignActorTarget");Scribe_Values.Look(ref CompletionKey,"completionKey");Scribe_Values.Look(ref Weapon,"weapon");Scribe_Values.Look(ref DraftOnArrival,"draftOnArrival");}
        public override void PostDestroy(DestroyMode mode,Map previousMap){
            if(parent.GetComp<CompWarpIn>().ticksToWarp!=0||previousMap==null)return;
            var c=Current.Game.GetComponent<CampaignController>();var placement=c.MapSpec.Objects.Single(p=>p.Id==Target);
            var actor=c.Place(placement,previousMap);
            if(actor is Pawn pawn&&DraftOnArrival){
                if(!string.IsNullOrEmpty(Weapon)&&pawn.equipment!=null){pawn.equipment.DestroyAllEquipment();pawn.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(Weapon)));}
                pawn.drafter??=new Pawn_DraftController(pawn);pawn.drafter.Drafted=true;pawn.drafter.FireAtWill=true;
            }
            if(!string.IsNullOrEmpty(CompletionKey))c.State.Variables[CompletionKey]=1;
            previousMap.GetComponent<SceneInteractionMapComponent>()?.RefreshBindings();
            SoundDefOf.Psycast_Skip_Exit.PlayOneShot(new TargetInfo(actor.Position,previousMap));
            FleckMaker.Static(actor.Position,previousMap,FleckDefOf.PsycastAreaEffect,2f);
        }
    }
    [CampaignModule("rc.pmcWarpUnit","Action","Warp a chapter-local PawnKind placement, drafted on arrival. Target placement, Text optional weapon, Value seconds.")]
    public sealed class WarpUnit:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){
            var c=(CampaignController)host;var placement=c.MapSpec.Objects.Single(p=>p.Id==a.Target);
            if(placement.Kind!="PawnKind"||placement.Faction!="Player")throw new InvalidOperationException("Unit warp requires a player PawnKind placement.");
            string key="unit-warp:"+a.Id;
            if(c.State.Variables.ContainsKey(key))return true;
            if(c.Objects.TryGetValue(key,out var previous)){
                if(previous?.Spawned!=true)throw new InvalidOperationException("Unit warp interrupted: "+placement.Id);
                return false;
            }
            var beacon=(ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RC_PMCActorBeacon"));
            var binding=beacon.GetComp<CompWarpActorBinding>();binding.Target=placement.Id;binding.CompletionKey=key;binding.DraftOnArrival=true;binding.Weapon=a.Text;
            beacon.GetComp<CompWarpIn>().Setup(null,Math.Max(1,(int)Math.Round((a.Value>0?a.Value:3)*60)));
            GenSpawn.Spawn(beacon,new IntVec3(placement.Position.X,0,placement.Position.Z),c.Map);c.Objects[key]=beacon;return false;
        }
    }
}
