using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimCampaign.Runtime
{
    public static class AcousticField
    {
        public static Dictionary<IntVec3,float> Costs(Map map,IntVec3 at,float maximum)
        {
            var costs=new Dictionary<IntVec3,float>();if(!at.InBounds(map)||maximum<0)return costs;
            var queue=new PriorityQueue<IntVec3,float>();costs[at]=0;queue.Enqueue(at,0);
            while(queue.TryDequeue(out var c,out var cost)){
                if(cost>costs[c]||cost>maximum)continue;
                foreach(var offset in GenAdj.CardinalDirections){var n=c+offset;if(!n.InBounds(map))continue;
                    var wall=n.GetEdifice(map);if(wall!=null&&wall.def.Fillage==FillCategory.Full&&!(wall is Building_Door))continue;
                    float step=wall is Building_Door door&&!door.Open?5:1;float nc=cost+step;
                    if(nc<=maximum&&(!costs.TryGetValue(n,out var old)||nc<old)){costs[n]=nc;queue.Enqueue(n,nc);}
                }
            }
            return costs;
        }
    }
    public partial class CampaignController
    {
        public int PlaySoundPulse(string origin,SoundPulseSpec spec,List<Act> response,string signal="")
        {
            if(Map==null||spec==null)return 0;
            Thing Resolve(string id)=>ResolveTriggerPawn(id)??(Objects.TryGetValue(id,out var thing)?thing:null);
            var emitter=Resolve(origin);var placement=MapSpec.Objects.FirstOrDefault(o=>o.Id==origin);
            var at=emitter?.Position??(placement==null?IntVec3.Invalid:new IntVec3(placement.Position.X,0,placement.Position.Z));
            if(!at.InBounds(Map))return 0;
            if(spec.Sound!=null&&!string.IsNullOrEmpty(spec.Sound.Name))DefDatabase<SoundDef>.GetNamed(spec.Sound.Name).PlayOneShot(new TargetInfo(at,Map));
            var stealth=StealthMapComponent.For(Map);
            var recipients=new Dictionary<Thing,string>();
            foreach(var binding in Objects)if(binding.Value?.Spawned==true)recipients[binding.Value]=binding.Key;
            foreach(var binding in Actors)if(binding.Value?.Spawned==true&&!recipients.ContainsKey(binding.Value))recipients[binding.Value]="role:"+binding.Key;
            var explicitTargets=new HashSet<Thing>((spec.Targets??new List<string>()).Select(Resolve).Where(t=>t!=null));
            var costs=spec.RespectWalls?AcousticField.Costs(Map,at,spec.Radius*5):null;int count=0;
            foreach(var pair in recipients){
                var thing=pair.Key;var pawn=thing as Pawn;var guard=stealth?.Guard(pawn);
                if(spec.Receivers=="Pawns"&&pawn==null||spec.Receivers=="Guards"&&guard==null||pawn?.Dead==true||pawn?.Downed==true)continue;
                if(spec.Targets?.Count>0&&!explicitTargets.Contains(thing))continue;
                var obj=MapSpec.Objects.FirstOrDefault(o=>o.Id==pair.Value);
                if(!string.IsNullOrEmpty(spec.Group)&&obj?.Group!=spec.Group)continue;
                if(!string.IsNullOrEmpty(spec.Faction)&&thing.Faction!=Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(MapSpec,spec.Faction))continue;
                float range=spec.Radius*(guard?.HearingMultiplier??1);
                if(spec.RespectWalls?(!costs.TryGetValue(thing.Position,out var cost)||cost>range):thing.Position.DistanceTo(at)>range)continue;
                if(guard!=null&&spec.GuardReaction)stealth.State.Guards[guard.Id].Hear(StealthMapComponent.Cell(at),Find.TickManager.TicksGame,guard,spec.Dangerous);
                // Each recipient gets an independent action sequence; $source resolves to that recipient.
                if(response!=null&&response.Count>0)engine.Enqueue(response,source:pair.Value);
                engine.Emit("SoundHeard",string.IsNullOrEmpty(signal)?origin:signal,pair.Value);count++;
            }
            return count;
        }
    }
}
