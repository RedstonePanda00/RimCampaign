using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime
{
    public sealed partial class StealthMapComponent
    {
        IEnumerable<IntVec3> PlayerVisionOrigins(Pawn pawn)
        {
            yield return pawn.Position;
            if(!Config.PlayerCornerPeek)yield break;
            bool Clear(IntVec3 cell)=>cell.InBounds(map)&&cell.Standable(map)&&
                !(cell.GetEdifice(map) is Building_Door door&&!door.Open);
            // Lean only beside an obstruction, into an immediately adjacent open cell.
            // Do not chain origins: this cannot scout down a corridor or through a wall.
            if(GenAdj.CardinalDirections.All(d=>Clear(pawn.Position+d)))yield break;
            foreach(var direction in GenAdj.CardinalDirections){
                var origin=pawn.Position+direction;
                if(Clear(origin)&&GenSight.LineOfSight(pawn.Position,origin,map,skipFirstCell:true))yield return origin;
            }
        }
    }
}
