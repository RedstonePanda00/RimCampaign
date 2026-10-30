using System;
using System.Linq;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime {
 static class TaskDropPods {
  public static void Drop(IntVec3 center,Map map,Thing thing,Faction faction,bool forbid=false){
   // Native landing checks allow some blueprints. Avoid active construction as well,
   // so a supply/reinforcement pod cannot temporarily obstruct a builder's frame job.
   foreach(var cell in GenRadial.RadialCellsAround(center,16,true)){
    if(!DropCellFinder.IsGoodDropSpot(cell,map,true,false)||cell.GetThingList(map).Any(t=>t is Blueprint||t is Frame))continue;
    var info=new ActiveTransporterInfo();info.innerContainer.TryAdd(thing);thing.SetForbidden(forbid,false);
    DropPodUtility.MakeDropPodAt(cell,map,info,faction);return;
   }
   throw new InvalidOperationException("No unobstructed drop-pod landing cell near "+center);
  }
 }
}
