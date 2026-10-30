using System;
using System.Collections.Generic;
using System.Linq;
using ProtossMech;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using Verse;

namespace RimCampaign.Integration.ProtossMech
{
    [CampaignModule("rc.pmcWarp","Action","使用 PMC 折跃指定单件物品（Value 为游戏秒）")]
    public sealed class WarpReward : IActionModule
    {
        public bool Execute(Act action,IHost host,string source,double delta)
        {
            var campaign=(CampaignController)host;
            string pending="warp:"+action.Id;
            if(campaign.Objects.TryGetValue(action.Target,out var delivered)&&delivered!=null&&!delivered.Destroyed){
                campaign.Objects.Remove(pending);return true;
            }
            if(campaign.Objects.TryGetValue(pending,out var existing)){
                if(existing==null||existing.Destroyed)throw new InvalidOperationException("Warp beacon interrupted: "+action.Target);
                return false;
            }
            var placement=campaign.MapSpec.Objects.Single(p=>p.Id==action.Target);
            var def=DefDatabase<ThingDef>.GetNamed(placement.Def.Name);
            if(def.category!=ThingCategory.Item||placement.Count!=1)throw new InvalidOperationException("PMC warp expects a single item placement.");
            var beacon=(ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RC_PMCRewardBeacon"));
            beacon.GetComp<CompWarpRewardBinding>().Target=action.Target;
            beacon.GetComp<CompWarpIn>().SetupThing(def.defName,Math.Max(1,(int)Math.Round((action.Value>0?action.Value:3)*60)),null,null);
            GenSpawn.Spawn(beacon,new IntVec3(placement.Position.X,0,placement.Position.Z),campaign.Map);
            campaign.Objects[pending]=beacon;
            return false;
        }
    }
    public sealed class CompProperties_WarpRewardBinding : CompProperties
    {
        public CompProperties_WarpRewardBinding(){compClass=typeof(CompWarpRewardBinding);}
    }
    // PMC owns the countdown, flecks, sound and item generation. This adapter
    // only binds that generated item back to the campaign's placement ID.
    public sealed class CompWarpRewardBinding : ThingComp
    {
        public string Target;
        IntVec3 cell;
        List<string> prior=new List<string>();
        public override void PostSpawnSetup(bool respawningAfterLoad){
            if(respawningAfterLoad)return;
            cell=parent.Position;prior=cell.GetThingList(parent.Map).Select(t=>t.ThingID).ToList();
        }
        public override void PostExposeData(){
            Scribe_Values.Look(ref Target,"campaignTarget");Scribe_Values.Look(ref cell,"deliveryCell");
            Scribe_Collections.Look(ref prior,"priorItems",LookMode.Value);
        }
        public override void PostDestroy(DestroyMode mode,Map previousMap){
            var warp=parent.GetComp<CompWarpIn>();
            if(warp.ticksToWarp!=0||previousMap==null)return;
            var item=cell.GetThingList(previousMap).SingleOrDefault(t=>t.def.defName==warp.thingDefNameToSpawn&&!prior.Contains(t.ThingID));
            if(item==null)throw new InvalidOperationException("PMC completed without delivering "+Target);
            Current.Game.GetComponent<CampaignController>().Objects[Target]=item;
        }
    }
}
