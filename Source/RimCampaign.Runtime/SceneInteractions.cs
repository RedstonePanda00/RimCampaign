using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimCampaign.Runtime {
    // Opt-in, non-stealth map interactions. Rewards are pre-generated stock owned by the controller.
    public sealed class SceneInteractionMapComponent : CustomMapComponent,ICampaignMapModule {
        public bool Enabled;
        public HashSet<string> Completed=new HashSet<string>();
        readonly Dictionary<Thing,List<SceneInteractionSpec>> hosts=new Dictionary<Thing,List<SceneInteractionSpec>>();
        Thing previewHost;
        int previewRevision=-1;
        readonly List<Tuple<List<IntVec3>,List<IntVec3>>> previews=new List<Tuple<List<IntVec3>,List<IntVec3>>>();
        CampaignController Controller=>CampaignController.ForMap(map);
        public SceneInteractionMapComponent(Map map):base(map){}
        public void Attach(CampaignController c,MapComponentSpec spec){Enabled=spec.Enabled;Rebind(c);}
        void Rebind(CampaignController c){hosts.Clear();foreach(var spec in c.MapSpec.Interactions??new List<SceneInteractionSpec>())if(c.Objects.TryGetValue(spec.Host,out var host)&&host!=null){if(!hosts.TryGetValue(host,out var rows))hosts[host]=rows=new List<SceneInteractionSpec>();rows.Add(spec);}}
        public override void FinalizeInit(){base.FinalizeInit();if(Controller?.Map==map)Rebind(Controller);}
        public void RefreshBindings(){if(Controller?.Map==map)Rebind(Controller);}
        public void SetEnabled(bool value){if(!value)Detach();Enabled=value;}
        public void Detach(){Enabled=false;foreach(var p in map.mapPawns.AllPawnsSpawned)if(p.jobs?.curDriver is JobDriver_SceneInteraction)p.jobs.EndCurrentJob(JobCondition.InterruptForced);}
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref Enabled,"enabled");Scribe_Collections.Look(ref Completed,"completed",LookMode.Value);}
        public SceneInteractionSpec FindSpec(string id)=>Controller?.MapSpec.Interactions?.FirstOrDefault(s=>s.Id==id);
        public bool Available(SceneInteractionSpec spec)=>Enabled&&spec!=null&&Controller?.Map==map&&!Controller.Presentation&&Controller.State.Phase=="Running"&&(!spec.OneShot||!Completed.Contains(spec.Id))&&Controller.TestCondition(spec.When);
        public bool Begin(SceneInteractionSpec spec,Pawn pawn){
            if(!Available(spec)||pawn?.Spawned!=true||pawn.Map!=map||pawn.Faction!=Faction.OfPlayer||pawn.Downed||pawn.Dead)return false;
            if(!Controller.Objects.TryGetValue(spec.Host,out var host)||host?.Spawned!=true||!pawn.CanReach(host,PathEndMode.Touch,Danger.Deadly)||!pawn.CanReserve(host))return false;
            var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RC_SceneInteraction"),host);job.playerForced=true;job.count=Controller.MapSpec.Interactions.IndexOf(spec);
            return pawn.jobs.TryTakeOrderedJob(job,JobTag.Misc);
        }
        public bool Complete(SceneInteractionSpec spec,Pawn pawn){
            if(!Available(spec)||!Controller.Objects.TryGetValue(spec.Host,out var host)||!pawn.CanReachImmediate(host,PathEndMode.Touch))return false;
            var blocked=new HashSet<IntVec3>(spec.BlockCells.Select(p=>new IntVec3(p.X,0,p.Z)));
            if(blocked.Count>0){
                var guide=Controller.ResolveTriggerPawn(map.GetComponent<EscortMapComponent>()?.Guide??"");
                if(blocked.Any(p=>!p.InBounds(map)||p.GetFirstPawn(map)!=null)||EscapePath(pawn.Position,spec.EscapeCell,blocked)==null||guide?.Spawned==true&&EscapePath(guide.Position,spec.EscapeCell,blocked)==null){
                    Messages.Message(RcText.T("无法在此刻放倒结构：需要留出安全通道。"),MessageTypeDefOf.RejectInput,false);return false;
                }
                var def=DefDatabase<ThingDef>.GetNamed(spec.ObstacleDef);
                foreach(var at in blocked){
                    if(at.GetEdifice(map)!=null)continue;
                    GenSpawn.Spawn(ThingMaker.MakeThing(def),at,map);
                    FleckMaker.ThrowDustPuff(at.ToVector3Shifted(),map,1.5f);
                }
                if(spec.DestroyHost)host.Destroy(DestroyMode.Vanish);
            }
            if(spec.OneShot)Completed.Add(spec.Id);
            Controller.QueueStory(spec.Actions,pawn.ThingID);Controller.Signal("InteractionCompleted",spec.Id,pawn.ThingID);return true;
        }
        List<IntVec3> EscapePath(IntVec3 origin,Cell destination,HashSet<IntVec3> blocked){
            if(destination==null)return new List<IntVec3>();
            var end=new IntVec3(destination.X,0,destination.Z);
            var open=new Queue<IntVec3>();var previous=new Dictionary<IntVec3,IntVec3>{{origin,origin}};open.Enqueue(origin);
            while(open.Count>0){var at=open.Dequeue();if(at==end){var path=new List<IntVec3>();while(at!=origin){path.Add(at);at=previous[at];}path.Add(origin);path.Reverse();return path;}foreach(var step in GenAdj.CardinalDirections){var next=at+step;if(next.InBounds(map)&&!blocked.Contains(next)&&next.Walkable(map)&&!previous.ContainsKey(next)){previous[next]=at;open.Enqueue(next);}}}
            return null;
        }
        public IEnumerable<FloatMenuOption> Options(Thing host,Pawn pawn){
            if(!Enabled||host.Fogged()||!hosts.TryGetValue(host,out var specs))yield break;
            foreach(var spec in specs)if(Available(spec))yield return new FloatMenuOption(RcText.T(spec.Label),()=>Begin(spec,pawn));
        }
        public IEnumerable<Gizmo> Commands(Thing host){
            if(!Enabled||host.Fogged()||!hosts.TryGetValue(host,out var specs))yield break;
            foreach(var spec in specs){
                var command=new Command_Action{defaultLabel=RcText.T(spec.Label),defaultDesc=RcText.T(spec.Name),icon=host.def.uiIcon,
                    action=()=>Find.Targeter.BeginTargeting(new TargetingParameters{canTargetPawns=true,canTargetLocations=false,canTargetBuildings=false,validator=t=>t.Thing is Pawn p&&p.Faction==Faction.OfPlayer},t=>Begin(spec,t.Pawn))};
                if(!Available(spec))command.Disable(RcText.T(Completed.Contains(spec.Id)?"本次操作已完成。":"尚未满足操作条件。"));yield return command;
            }
        }
        public override void MapComponentUpdate(){
            if(!Enabled||Find.CurrentMap!=map||Find.Selector.SingleSelectedThing is not Thing selected||!hosts.TryGetValue(selected,out var specs)){previewHost=null;return;}
            // Compute on selection/change, not on every repaint. Completion always rechecks live geometry.
            if(previewHost!=selected||previewRevision!=Completed.Count){
                previewHost=selected;previewRevision=Completed.Count;previews.Clear();
                foreach(var spec in specs)if(spec.BlockCells.Count>0){
                    var blocked=spec.BlockCells.Select(p=>new IntVec3(p.X,0,p.Z)).ToList();
                    var origin=GenAdj.CellsAdjacent8Way(selected).FirstOrDefault(at=>at.InBounds(map)&&at.Walkable(map)&&!blocked.Contains(at));
                    var path=EscapePath(origin,spec.EscapeCell,new HashSet<IntVec3>(blocked));
                    previews.Add(Tuple.Create(blocked,path?.Take(24).ToList()??new List<IntVec3>()));
                }
            }
            foreach(var preview in previews){GenDraw.DrawFieldEdges(preview.Item1,Color.red);if(preview.Item2.Count>0)GenDraw.DrawFieldEdges(preview.Item2,Color.green);}
        }
    }
    public sealed class JobDriver_SceneInteraction : JobDriver {
        string interaction;
        SceneInteractionMapComponent Component=>pawn.Map?.GetComponent<SceneInteractionMapComponent>();
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref interaction,"interaction");}
        public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.Reserve(job.targetA,job,1,-1,null,errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils(){
            if(interaction==null)interaction=CampaignController.ForMap(pawn.Map).MapSpec.Interactions.ElementAtOrDefault(job.count)?.Id;
            this.FailOn(()=>Component==null||!Component.Available(Component.FindSpec(interaction))||pawn.Downed||pawn.Dead||job.targetA.Thing?.Spawned!=true);
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
            var use=Toils_General.Wait(Component.FindSpec(interaction)?.UseTicks??1,TargetIndex.A);use.WithProgressBarToilDelay(TargetIndex.A);
            use.FailOn(()=>!pawn.CanReachImmediate(job.targetA,PathEndMode.Touch));yield return use;
            var done=ToilMaker.MakeToil("SceneInteractionComplete");done.defaultCompleteMode=ToilCompleteMode.Instant;
            done.initAction=()=>Component?.Complete(Component.FindSpec(interaction),pawn);yield return done;
        }
    }
    public sealed class FloatMenuOptionProvider_SceneInteraction : FloatMenuOptionProvider {
        protected override bool Drafted=>true;protected override bool Undrafted=>true;protected override bool Multiselect=>false;protected override bool RequiresManipulation=>true;
        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing,FloatMenuContext context){var module=clickedThing.Map?.GetComponent<SceneInteractionMapComponent>();if(module!=null)foreach(var option in module.Options(clickedThing,context.FirstSelectedPawn))yield return option;}
    }
    [HarmonyPatch(typeof(Building),nameof(Building.GetGizmos))]
    static class SceneInteractionGizmos {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values,Building __instance){foreach(var g in values)yield return g;var module=__instance.Map?.GetComponent<SceneInteractionMapComponent>();if(module!=null)foreach(var g in module.Commands(__instance))yield return g;}
    }
}
