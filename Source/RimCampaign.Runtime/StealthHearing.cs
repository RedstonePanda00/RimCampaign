using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Runtime
{
    public sealed partial class StealthMapComponent
    {
        readonly HashSet<Pawn> heardPawns=new HashSet<Pawn>();
        public bool Heard(Pawn pawn)=>Active&&Config.PlayerHearing&&pawn!=null&&heardPawns.Contains(pawn);
        void RefreshHearing()
        {
            heardPawns.Clear();
            var actor=Actor;
            if(!Config.PlayerHearing||actor?.Spawned!=true||actor.Dead||actor.Downed)return;
            foreach(var pawn in map.mapPawns.AllPawnsSpawned)
                if(!pawn.Dead&&pawn.HostileTo(actor)&&pawn.Position.DistanceToSquared(actor.Position)<=Config.PlayerSight*Config.PlayerSight)
                    heardPawns.Add(pawn);
        }
    }
    // Alter both cached and uncached body positions; modifying RenderPawnAt's input
    // alone is too late after the parallel pre-render step has cached its draw data.
    [HarmonyPatch(typeof(PawnRenderer),"GetBodyPos")]
    static class StealthHeardPawnAltitude
    {
        static void Postfix(Pawn ___pawn,ref Vector3 __result)
        {
            var stealth=StealthMapComponent.For(___pawn);
            if(stealth?.Heard(___pawn)==true&&!stealth.Visible(___pawn.Position))
                __result.y=AltitudeLayer.MetaOverlays.AltitudeFor()+.02f;
        }
    }
}
