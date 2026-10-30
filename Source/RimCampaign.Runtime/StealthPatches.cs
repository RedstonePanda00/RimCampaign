using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimCampaign.Runtime
{
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.SpawnSetup))]
    static class StealthActorArrival
    {
        static void Postfix(Pawn __instance){
            var stealth=StealthMapComponent.For(__instance);
            if(stealth?.Actor==__instance)stealth.CaptureControl();
        }
    }
    // Keep normal maps and unmanaged pawns on their original think trees.
    [HarmonyPatch(typeof(Pawn_JobTracker),"DetermineNextJob")]
    static class StealthThinking
    {
        static bool Prefix(Pawn ___pawn,ref ThinkTreeDef __0,ref ThinkResult __result)
        {
            var c=StealthMapComponent.For(___pawn);
            if(c==null||c.Guard(___pawn)==null||___pawn.Dead||___pawn.Downed)return true;
            __0=null;__result=ThinkResult.NoJob;return false;
        }
    }
    [HarmonyPatch(typeof(AttackTargetFinder),nameof(AttackTargetFinder.BestAttackTarget))]
    static class StealthAcquisition
    {
        static void Prefix(IAttackTargetSearcher searcher,ref Predicate<Thing> validator)
        {
            var pawn=searcher.Thing as Pawn;var c=StealthMapComponent.For(pawn);if(c==null)return;
            var previous=validator;var guard=c.Guard(pawn);
            if(guard!=null)validator=t=>(previous==null||previous(t))&&t==c.Actor&&c.CanAttackActor(guard);
            else if(pawn==c.Actor)validator=t=>(previous==null||previous(t))&&c.PlayerCanTarget(t);
        }
    }
    [HarmonyPatch(typeof(Verb),nameof(Verb.TryStartCastOn),new[]{typeof(LocalTargetInfo),typeof(LocalTargetInfo),typeof(bool),typeof(bool),typeof(bool),typeof(bool)})]
    static class StealthShotPermission
    {
        static bool Prefix(Verb __instance,LocalTargetInfo castTarg,ref bool __result)
        {
            var p=__instance.CasterPawn;var c=StealthMapComponent.For(p);if(c==null)return true;
            var g=c.Guard(p);
            if((g!=null&&(castTarg.Thing!=c.Actor||!c.CanAttackActor(g)))||(p==c.Actor&&castTarg.HasThing&&!c.PlayerCanTarget(castTarg.Thing))){__result=false;return false;}
            return true;
        }
        static void Postfix(Verb __instance,bool __result)
        {
            if(!__result||__instance.verbProps.IsMeleeAttack||__instance.verbProps.defaultProjectile==null)return;
            var p=__instance.CasterPawn;var c=StealthMapComponent.For(p);if(c==null||p==null)return;
            if(p==c.Actor)c.State.PlayerNoiseAt=Find.TickManager.TicksGame;
            c.EmitNoise(p.Position,c.Config.GunshotRadius,true);
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.PostApplyDamage))]
    static class StealthHitResponse
    {
        static void Postfix(Pawn __instance,DamageInfo dinfo,float totalDamageDealt)
        {
            var c=StealthMapComponent.For(__instance);var g=c?.Guard(__instance);
            if(g==null||totalDamageDealt<=0)return;
            var memory=c.State.Guards[g.Id];
            // Damage is evidence of attack, not privileged access to the instigator's position.
            memory.Hear(StealthMapComponent.Cell(__instance.Position),Find.TickManager.TicksGame,g,true);
            if(c.SeesActor(g))memory.Observe(StealthMapComponent.Cell(c.Actor.Position),Find.TickManager.TicksGame,1,g,true);
            if(memory.Identified)CampaignController.ForMap(__instance.Map)?.RecordPerformance("detected","guard:"+g.Id);
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.GetGizmos))]
    static class StealthGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values,Pawn __instance)
        {
            foreach(var g in values)yield return g;
            var c=StealthMapComponent.For(__instance);if(c!=null)foreach(var g in c.Commands(__instance))yield return g;
        }
    }
    [HarmonyPatch(typeof(PawnRenderer),nameof(PawnRenderer.RenderPawnAt))]
    static class StealthPawnRendering
    {
        static bool Prefix(Pawn ___pawn,ref Vector3 drawLoc,ref Rot4? rotOverride)
        {
            var c=StealthMapComponent.For(___pawn);if(c==null)return true;
            if(!c.PlayerCanTarget(___pawn))return false;
            if(c.State.PendingKind=="Takedown"&&___pawn==c.Actor&&c.Resolve(c.State.PendingTarget) is Pawn target){
                // Generic paired restraint pose. Character-specific renderers can replace this presentation.
                float progress=Mathf.Clamp01((float)c.State.PendingProgress/c.Config.TakedownTicks);
                var direction=(target.DrawPos-drawLoc);direction.y=0;
                drawLoc+=direction*Mathf.Min(.55f,progress*3);rotOverride=target.Rotation;
                drawLoc.x+=Mathf.Sin(progress*40)*.035f;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(FogGrid),nameof(FogGrid.Notify_PawnEnteringDoor))]
    static class StealthDoorFog
    {
        static bool Prefix(Map ___map)=>StealthMapComponent.For(___map)?.Config.DynamicFog!=true;
    }
    [HarmonyPatch(typeof(FogGrid),nameof(FogGrid.FloodUnfogAdjacent),new[]{typeof(IntVec3),typeof(bool)})]
    static class StealthCellFog
    {
        static bool Prefix(Map ___map)=>StealthMapComponent.For(___map)?.Config.DynamicFog!=true;
    }
    [HarmonyPatch(typeof(FogGrid),nameof(FogGrid.FloodUnfogAdjacent),new[]{typeof(Thing),typeof(bool)})]
    static class StealthThingFog
    {
        static bool Prefix(Map ___map)=>StealthMapComponent.For(___map)?.Config.DynamicFog!=true;
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.DrawGUIOverlay))]
    static class StealthPawnLabels
    {
        static bool Prefix(Pawn __instance)=>StealthMapComponent.For(__instance)?.PlayerCanTarget(__instance)!=false;
    }
    // Vanilla hover text and target picking also use this gate; do not expose hidden
    // pawns just because their remembered terrain is no longer in native black fog.
    [HarmonyPatch(typeof(InvisibilityUtility),nameof(InvisibilityUtility.IsHiddenFromPlayer))]
    static class StealthPlayerInformation
    {
        static void Postfix(Pawn __0,ref bool __result){var c=StealthMapComponent.For(__0);if(c!=null&&!c.PlayerCanTarget(__0))__result=true;}
    }
    [HarmonyPatch(typeof(Selector),nameof(Selector.Select))]
    static class StealthSelection
    {
        static bool Prefix(object obj)=>!(obj is Thing t)||StealthMapComponent.For(t.MapHeld)?.PlayerCanTarget(t)!=false;
    }
    [HarmonyPatch(typeof(CompressibilityDecider),"DetermineReferences")]
    static class StealthHostSave
    {
        static void Postfix(Map ___map,HashSet<Thing> ___referencedThings)
        {
            var c=StealthMapComponent.For(___map);if(c==null)return;
            foreach(var d in c.Config.Devices){var t=c.Resolve(d.Host);if(t?.Spawned==true)___referencedThings.Add(t);}
        }
    }
}
