using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime
{
    static class Overrides
    {
        public static CampaignController For(Pawn pawn) {
            var c=CampaignController.ForMap(pawn?.MapHeld);
            return c?.StoryActive==true&&!c.Transitioning&&pawn!=null&&pawn.Faction==Faction.OfPlayer&&pawn.Map==c.Map?c:null;
        }
    }
    [HarmonyPatch(typeof(Need_Food),nameof(Need_Food.NeedInterval))]
    static class HungerOverride
    {
        static bool Prefix(Need_Food __instance,Pawn ___pawn){
            if(Overrides.For(___pawn)?.Node.Hunger!=false)return true;
            __instance.CurLevelPercentage=1;return false;
        }
    }
    [HarmonyPatch(typeof(Need_Mood),nameof(Need_Mood.NeedInterval))]
    static class MoodOverride
    {
        static bool Prefix(Need_Mood __instance,Pawn ___pawn){
            if(Overrides.For(___pawn)?.Node.Mood!=false)return true;
            __instance.CurLevelPercentage=1;return false;
        }
    }
    [HarmonyPatch(typeof(Pawn_WorkSettings),nameof(Pawn_WorkSettings.GetPriority))]
    static class WorkOverride
    {
        static void Postfix(WorkTypeDef w,Pawn ___pawn,ref int __result){
            var c=Overrides.For(___pawn);if(c==null)return;
            if(!c.Node.Work||(!c.Node.Construction&&w==WorkTypeDefOf.Construction)||(!c.Node.Research&&w==WorkTypeDefOf.Research))__result=0;
        }
    }
    [HarmonyPatch(typeof(Storyteller),nameof(Storyteller.StorytellerTick))]
    static class StorytellerOverride
    {
        static bool Prefix(){var c=Current.Game?.GetComponent<CampaignController>();return c?.StoryActive!=true||c.Node.Storyteller;}
    }
    [HarmonyPatch(typeof(Selector),nameof(Selector.Select))]
    static class CinematicSelection
    {
        static bool Prefix(){var c=CampaignController.Focused;return c?.Presentation!=true;}
    }
    [HarmonyPatch(typeof(HistoryAutoRecorder),nameof(HistoryAutoRecorder.Tick))]
    static class PocketHistoryBaseline
    {
        static bool Prefix(HistoryAutoRecorder __instance){
            // A campaign can begin without ever having a home colony. Vanilla assumes
            // there is already a last history sample when no home map is available.
            if(Current.Game?.GetComponent<CampaignController>()?.Active!=true||Find.AnyPlayerHomeMap!=null||__instance.records.Count!=0)return true;
            __instance.records.Add(0f);return false;
        }
    }
    [HarmonyPatch(typeof(GameComponent_Anomaly),nameof(GameComponent_Anomaly.StartedNewGame))]
    static class PocketAnomalyInitialization
    {
        // Preserve vanilla level/research initialization; only provide a map for its monolith lookup.
        static Map StartingMap()=>Find.AnyPlayerHomeMap??(Current.Game?.GetComponent<CampaignController>()?.Active==true?Current.Game.GetComponent<CampaignController>().Map:null);
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter=AccessTools.PropertyGetter(typeof(Find),nameof(Find.AnyPlayerHomeMap));
            foreach(var instruction in instructions){
                if(instruction.Calls(getter)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(PocketAnomalyInitialization),nameof(StartingMap));}
                yield return instruction;
            }
        }
    }
}
