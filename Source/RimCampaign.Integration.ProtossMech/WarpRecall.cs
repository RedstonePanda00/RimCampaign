using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimCampaign.Integration.ProtossMech {
    [CampaignModule("rc.pmcRecall","Action","Recall surviving player pawns in place using PMC's existing stationary resource extraction fleck. Runs in game time.")]
    public sealed class WarpRecall:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){
            var c=(CampaignController)host;string key="recall:"+a.Id;
            var fleck=DefDatabase<FleckDef>.GetNamed("PM_WarpResourceExtractionFleck");
            if(!c.State.Variables.TryGetValue(key,out var elapsed)){
                c.State.Variables[key]=0;
                foreach(var pawn in c.Map.mapPawns.AllPawnsSpawned.Where(p=>p.Faction==Faction.OfPlayer&&!p.Dead).ToList()){
                    c.Objects[key+":"+pawn.ThingID]=pawn;
                    pawn.jobs?.StopAll();pawn.pather?.StopDead();
                    var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=(int)(fleck.Lifetime*60)+2;pawn.jobs?.StartJob(wait,JobCondition.InterruptForced);
                    // Match ResourcePaymentUtility's stationary sprite alignment.
                    FleckMaker.Static(pawn.DrawPos+new Vector3(0,0,3),c.Map,fleck,.5f);
                    SoundDefOf.Psycast_Skip_Entry.PlayOneShot(new TargetInfo(pawn.Position,c.Map));
                }
                return false;
            }
            elapsed+=delta;c.State.Variables[key]=elapsed;
            if(elapsed<fleck.Lifetime)return false;
            foreach(var pair in c.Objects.Where(p=>p.Key.StartsWith(key+":")))if(pair.Value?.Spawned==true&&pair.Value.Map==c.Map)pair.Value.DeSpawn();
            return true;
        }
    }
}
