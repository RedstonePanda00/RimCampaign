using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime
{
    // The vanilla container owns contents, opening state, empty graphics and save/load.
    [HarmonyPatch(typeof(Building_Crate),nameof(Building_Crate.Open))]
    static class CampaignCrateOpened
    {
        static void Prefix(Building_Crate __instance,out bool __state)=>__state=__instance.CanOpen&&__instance.HasAnyContents;
        static void Postfix(Building_Crate __instance,bool __state){
            if(!__state||__instance.HasAnyContents)return;
            var c=StealthMapComponent.For(__instance.Map);if(c==null)return;
            var d=c.Config.Devices.FirstOrDefault(x=>x.Kind=="SupplyCrate"&&c.Resolve(x.Host)==__instance);if(d==null)return;
            var executor=__instance.Map.mapPawns.AllPawnsSpawned.FirstOrDefault(p=>p.CurJob?.targetA.Thing==__instance&&(p.jobs.curDriver is JobDriver_UseCampaignDevice||p.CurJob.def==JobDefOf.Open));
            c.EmitDeviceCompleted(d,executor);
        }
    }
}
