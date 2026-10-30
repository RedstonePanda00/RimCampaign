using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimCampaign.Runtime
{
    public class StealthDeviceState
    {
        public bool Captured { get; set; }
        public bool Disabled { get; set; }
        public int ReportProgress { get; set; }
        public int DetectionProgress { get; set; }
        public bool Identified { get; set; }
        public bool Reported { get; set; }
        public bool Opened { get; set; }
    }
    public class StealthState
    {
        public Dictionary<string,GuardMemory> Guards { get; set; } = new Dictionary<string,GuardMemory>();
        public Dictionary<string,string> Bindings { get; set; } = new Dictionary<string,string>();
        public Dictionary<string,StealthDeviceState> Devices { get; set; } = new Dictionary<string,StealthDeviceState>();
        public List<string> Unconscious { get; set; } = new List<string>();
        public List<int> Explored { get; set; } = new List<int>();
        public bool AllowAutomaticMelee { get; set; }
        public string LeakSource { get; set; } = "";
        public bool Leaked { get; set; }
        public bool FirstReportWarned { get; set; }
        public string ReportWarningGuard { get; set; } = "";
        public string PendingKind { get; set; } = "";
        public string PendingTarget { get; set; } = "";
        public int PendingProgress { get; set; }
        public int PlayerNoiseAt { get; set; } = -1000;
    }
    [StaticConstructorOnStartup]
    public sealed partial class StealthMapComponent : CustomMapComponent, ICampaignMapModule
    {
        public StealthSpec Config;
        public StealthState State = new StealthState();
        readonly HashSet<int> visible = new HashSet<int>(), explored = new HashSet<int>();
        readonly Dictionary<string,Thing> resolved = new Dictionary<string,Thing>();
        bool rebuildFog = true;
        public StealthMapComponent(Map map) : base(map) { }
        bool originalDrafted, originalFireAtWill, controlsCaptured;
        Dictionary<string,int> ownedJobs=new Dictionary<string,int>();
        List<int> originalFog;
        public void Attach(CampaignController controller, MapComponentSpec spec) {
            var config=Json.Copy(controller.MapSpec.Stealth);config.Enabled=spec.Enabled;
            Configure(config,controller.Objects,controller.Actors);
        }
        public void SetEnabled(bool enabled) {
            if(Config==null)return;
            if(!enabled){Config.Enabled=false;ReleaseControl();}
            else {Config.Enabled=true;CaptureControl();}
            rebuildFog=true;
        }
        internal void CaptureControl(){
            if(originalFog==null)originalFog=Enumerable.Range(0,map.cellIndices.NumGridCells).Where(i=>map.fogGrid.IsFogged(i)).ToList();
            if(controlsCaptured||Actor?.drafter==null)return;
            originalDrafted=Actor.Drafted;originalFireAtWill=Actor.drafter.FireAtWill;controlsCaptured=true;
            Actor.drafter.Drafted=true;Actor.drafter.FireAtWill=false;
        }
        public void Detach(){SetEnabled(false);}
        void ReleaseControl(){
            if(reportLetter!=null){Find.LetterStack.RemoveLetter(reportLetter);reportLetter=null;}
            foreach(var pawn in map.mapPawns.AllPawnsSpawned){
                if(ownedJobs.TryGetValue(pawn.ThingID,out var id)&&pawn.CurJob?.loadID==id){pawn.jobs.StopAll();pawn.pather.StopDead();}
                if(Config.Guards.Any(g=>Resolve(g.Pawn)==pawn)&&pawn.mindState.enemyTarget==Actor)pawn.mindState.enemyTarget=null;
            }
            ownedJobs.Clear();
            if(controlsCaptured&&Actor?.drafter!=null){
                if(!Actor.drafter.FireAtWill)Actor.drafter.FireAtWill=originalFireAtWill;
                if(Actor.Drafted&&!originalDrafted)Actor.drafter.Drafted=false;
            }
            if(originalFog!=null){map.fogGrid.ClearAllFog();foreach(int index in originalFog)map.fogGrid.Refog(new CellRect(map.cellIndices.IndexToCell(index).x,map.cellIndices.IndexToCell(index).z,1,1));originalFog=null;}
            controlsCaptured=false;
            State.ReportWarningGuard="";State.PendingKind="";State.PendingTarget="";
            visible.Clear();heardPawns.Clear();
        }
        void Give(Pawn pawn,Job job){ownedJobs[pawn.ThingID]=job.loadID;pawn.jobs.StartJob(job,JobCondition.InterruptForced);}
        public bool Active => Config?.Enabled == true;
        public Pawn Actor => Resolve(Config?.Actor) as Pawn;
        public static StealthMapComponent For(Map map) { var c=map?.GetComponent<StealthMapComponent>();return c?.Active==true?c:null; }
        public static StealthMapComponent For(Pawn pawn) => For(pawn?.MapHeld);
        public GuardSpec Guard(Pawn pawn) => Active&&pawn!=null?Config.Guards.FirstOrDefault(g=>Resolve(g.Pawn)==pawn):null;
        public bool Sleeping(Pawn pawn) => Active&&pawn!=null&&State.Unconscious.Contains(pawn.ThingID);
        public void Configure(StealthSpec spec, IDictionary<string,Thing> objects, IDictionary<string,Pawn> actors)
        {
            if(Config!=null)SetEnabled(false);
            resolved.Clear();Config=Json.Copy(spec);State=new StealthState();
            foreach(var p in objects)if(p.Value!=null)State.Bindings[p.Key]=p.Value.ThingID;
            foreach(var p in actors)if(p.Value!=null){State.Bindings[p.Key]=p.Value.ThingID;State.Bindings["role:"+p.Key]=p.Value.ThingID;}
            foreach(var g in spec.Guards)State.Guards[g.Id]=new GuardMemory{Id=g.Id};
            foreach(var d in spec.Devices)State.Devices[d.Id]=new StealthDeviceState{Captured=d.StartsCaptured};
            resolved.Clear();visible.Clear();explored.Clear();heardPawns.Clear();rebuildFog=true;
            if(Active)CaptureControl();
            RefreshVision();
        }
        public Thing Resolve(string id)
        {
            if(string.IsNullOrEmpty(id))return null;
            if(resolved.TryGetValue(id,out var found)&&!found.Destroyed)return found;
            string thingId=State.Bindings.TryGetValue(id,out var binding)?binding:id;
            found=map.listerThings.AllThings.FirstOrDefault(t=>t.ThingID==thingId);
            // Authored actors may be waiting off-map for a scripted arrival.
            var campaign=CampaignController.ForMap(map);
            if(found==null&&campaign?.Map==map)found=campaign.Actors.Values.FirstOrDefault(p=>p.ThingID==thingId);
            if(found!=null)resolved[id]=found;
            return found;
        }
        public override void ExposeData()
        {
            if(Scribe.mode==LoadSaveMode.Saving&&reportLetter!=null&&!Find.LetterStack.LettersListForReading.Contains(reportLetter))reportLetter=null;
            Scribe_References.Look(ref reportLetter,"rcStealthReportLetter");
            Scribe_Values.Look(ref originalDrafted,"rcStealthOriginalDrafted");
            Scribe_Values.Look(ref originalFireAtWill,"rcStealthOriginalFireAtWill");
            Scribe_Values.Look(ref controlsCaptured,"rcStealthControlsCaptured");
            Scribe_Collections.Look(ref originalFog,"rcStealthOriginalFog",LookMode.Value);
            Scribe_Collections.Look(ref ownedJobs,"rcStealthOwnedJobs",LookMode.Value,LookMode.Value);
            ExposeConversations();
            string spec=Config==null?null:Json.Write(Config),state=Json.Write(State);
            if(Scribe.mode==LoadSaveMode.Saving){State.Explored=explored.ToList();state=Json.Write(State);}
            ScribeJson.Look(ref spec,"rcStealthConfig");ScribeJson.Look(ref state,"rcStealthState");
            if(Scribe.mode==LoadSaveMode.LoadingVars){Config=spec==null?null:Json.Read<StealthSpec>(spec);State=state==null?new StealthState():Json.Read<StealthState>(state);}
            if(Scribe.mode==LoadSaveMode.PostLoadInit){resolved.Clear();visible.Clear();explored.Clear();heardPawns.Clear();explored.UnionWith(State.Explored);rebuildFog=true;}
        }
        public static Cell Cell(IntVec3 c)=>new Cell{X=c.x,Z=c.z};
        public static IntVec3 Position(Cell c)=>c==null?IntVec3.Invalid:new IntVec3(c.X,0,c.Z);
        public IntVec3 DevicePosition(DeviceSpec d)=>Resolve(d.Host)?.Position??Position(d.Position);
        bool DeviceExists(DeviceSpec d)=>string.IsNullOrEmpty(d.Host)||Resolve(d.Host)?.Spawned==true;
        public bool DeviceWorking(DeviceSpec d)=>d!=null&&DeviceExists(d)&&!State.Devices[d.Id].Disabled;
        public bool CanSee(IntVec3 from,IntVec3 to,int facing,float range,float angle)
            => from.InBounds(map)&&to.InBounds(map)&&StealthMath.InCone(Cell(from),Cell(to),facing,range,angle)&&GenSight.LineOfSight(from,to,map,skipFirstCell:true);
        public static bool PartialConcealment(ThingDef def)=>def.category==ThingCategory.Building&&def.fillPercent>0&&def.fillPercent<1&&def.passability==Traversability.PassThroughOnly;
        public bool GuardCanSee(GuardSpec g,IntVec3 target){
            var guard=Resolve(g.Pawn) as Pawn;
            var memory=State.Guards[g.Id];
            // An engaged guard tracks a visible opponent while turning to aim.
            float angle=memory.Mode=="Combat"?360:g.ViewAngle;
            if(guard?.Spawned!=true||!CanSee(guard.Position,target,guard.Rotation.AsInt,g.SightRange,angle))return false;
            if(!Config.PartialCoverConcealsFromCalmGuards)return true;
            // Investigating a harmless noise is not an alarm. Yellow alert, search and combat are.
            if(memory.Alarmed||memory.Suspicion>=.5||memory.Mode=="Search"||memory.Mode=="Combat"||memory.Mode=="Reporting")return true;
            return GenSight.LineOfSight(guard.Position,target,map,skipFirstCell:true,
                validator:cell=>!cell.GetThingList(map).Any(t=>PartialConcealment(t.def)));
        }
        public bool SeesActor(GuardSpec g)
        {
            var guard=Resolve(g.Pawn) as Pawn;var actor=Actor;
            return guard?.Spawned==true&&!guard.Dead&&!guard.Downed&&!Sleeping(guard)&&actor?.Spawned==true&&!actor.Dead&&
                !actor.IsPsychologicallyInvisible()&&GuardCanSee(g,actor.Position);
        }
        public bool CanAttackActor(GuardSpec g)=>State.Guards.TryGetValue(g.Id,out var m)&&m.Identified&&m.Suspicion>=1&&m.Mode=="Combat"&&string.IsNullOrEmpty(m.ScriptedAction)&&SeesActor(g);
        bool CinematicReveal=>CampaignController.ForMap(map)?.RevealingMap==true;
        public bool Visible(IntVec3 c)=>CinematicReveal||!Active||!Config.DynamicFog||(c.InBounds(map)&&visible.Contains(map.cellIndices.CellToIndex(c)));
        public bool WasExplored(IntVec3 c)=>!Active||!Config.DynamicFog||(c.InBounds(map)&&explored.Contains(map.cellIndices.CellToIndex(c)));
        public bool Remembered(IntVec3 c)=>Active&&Config.DynamicFog&&WasExplored(c)&&!Visible(c);
        public bool PlayerCanTarget(Thing t)=>t==null||t==Actor||t is Pawn p&&Heard(p)||Visible(t.PositionHeld);
        public void RefreshVision()
        {
            if(!Active||CinematicReveal)return;
            var next=new HashSet<int>();
            void Add(IntVec3 center,int facing,float range,float angle,bool outdoorsOnly=false,IntVec3? rangeOrigin=null){
                if(!center.InBounds(map))return;
                foreach(var c in GenRadial.RadialCellsAround(center,range,true))if(c.InBounds(map)&&(!rangeOrigin.HasValue||c.DistanceToSquared(rangeOrigin.Value)<=range*range)&&(!outdoorsOnly||!c.Roofed(map))&&CanSee(center,c,facing,range,angle))next.Add(map.cellIndices.CellToIndex(c));
            }
            foreach(var p in map.mapPawns.AllPawnsSpawned.Where(CanOperate))
                foreach(var origin in PlayerVisionOrigins(p))Add(origin,0,Config.PlayerSight,360,rangeOrigin:p.Position);
            foreach(var d in Config.Devices)if(d.Kind=="Camera"&&DeviceWorking(d)&&State.Devices[d.Id].Captured)Add(DevicePosition(d),d.Facing,d.SightRange,d.ViewAngle,d.OutdoorsOnly);
            var reporter=WarningReporter;
            if(reporter!=null)Add(reporter.Position,0,6,360);
            RefreshHearing();
            if(Config.DynamicFog){
                if(rebuildFog){
                    map.fogGrid.Refog(new CellRect(0,0,map.Size.x,map.Size.z));
                    foreach(int index in explored)if(index>=0&&index<map.cellIndices.NumGridCells)map.fogGrid.Unfog(map.cellIndices.IndexToCell(index));
                    visible.Clear();rebuildFog=false;memoryShadeDirty=true;
                }
                // Native fog is discovery, not current sight. Remembered terrain stays revealed.
                foreach(int index in next)if(!explored.Contains(index))map.fogGrid.Unfog(map.cellIndices.IndexToCell(index));
                // Allow native pawn drawing/culling, but do not add heard cells to explored.
                // Our terrain fog stays opaque while the pawn is drawn above it.
                foreach(var pawn in heardPawns)map.fogGrid.Unfog(pawn.Position);
            }
            if(!visible.SetEquals(next))memoryShadeDirty=true;
            visible.Clear();visible.UnionWith(next);explored.UnionWith(next);
            if(Find.CurrentMap==map)foreach(var thing in Find.Selector.SelectedObjects.OfType<Thing>().ToList())if(!PlayerCanTarget(thing))Find.Selector.Deselect(thing);
        }
        public void Emit(string kind,string target,string source="")
        {
            var c=CampaignController.ForMap(map);
            if(c?.Active==true&&c.Map==map&&!c.Transitioning&&!c.EntryPending)c.State.Signals.Add(new RimCampaign.Core.Signal{Kind=kind,Target=target,Source=source});
        }
        bool Running
        {
            get { var c=CampaignController.ForMap(map);return c?.Active!=true||c.Map!=map||(!c.Freeze&&!c.Cinematic&&c.State.Phase=="Running"); }
        }
        public override void MapComponentTick()
        {
            if(!Active||!Running)return;
            int tick=Find.TickManager.TicksGame;
            TickInteraction();
            if(tick%Config.UpdateTicks!=0)return;
            RefreshVision();TickCameras(tick);
            foreach(var g in Config.Guards)TickGuard(g,tick);
            TickConversations(tick);
        }
        public void EmitNoise(IntVec3 at,float radius,bool dangerous=false)
        {
            if(!Active||!at.InBounds(map)||radius<=0)return;
            // Shortest acoustic paths cannot cross solid walls; closed doors attenuate sound.
            var costs=AcousticField.Costs(map,at,radius*Config.Guards.Select(g=>g.HearingMultiplier).DefaultIfEmpty(1).Max());
            foreach(var g in Config.Guards){var p=Resolve(g.Pawn) as Pawn;if(p?.Spawned!=true||p.Dead||p.Downed||Sleeping(p))continue;
                if(costs.TryGetValue(p.Position,out var cost)&&cost<=radius*g.HearingMultiplier&&State.Guards[g.Id].Hear(Cell(at),Find.TickManager.TicksGame,g,dangerous))Emit("NoiseHeard",g.Id);
            }
            Emit("Noise",dangerous?"danger":"distraction");
        }
        void TickCameras(int tick)
        {
            foreach(var d in Config.Devices.Where(d=>d.Kind=="Camera")){
                var s=State.Devices[d.Id];
                if(!DeviceWorking(d)||s.Captured){s.DetectionProgress=0;s.ReportProgress=0;continue;}
                if(s.Reported)continue;
                bool sees=Actor?.Spawned==true&&!Actor.Dead&&(!d.OutdoorsOnly||!Actor.Position.Roofed(map))&&!Actor.IsPsychologicallyInvisible()&&CanSee(DevicePosition(d),Actor.Position,d.Facing,d.SightRange,d.ViewAngle);
                s.DetectionProgress=sees?Math.Min(d.DetectionTicks,s.DetectionProgress+Config.UpdateTicks):0;
                if(sees&&s.DetectionProgress>=d.DetectionTicks){
                    if(!s.Identified)Emit("CameraDetected",d.Id);s.Identified=true;
                    CampaignController.ForMap(map)?.RecordPerformance("detected","camera:"+d.Id);
                    // A local camera supplies a timestamped sighting only to conscious receivers.
                    // Autonomous external transmission requires an explicit authoring option.
                    if(!d.AutoReport)foreach(var g in Config.Guards){
                        var p=Resolve(g.Pawn) as Pawn;
                        if(p?.Spawned==true&&!p.Dead&&!p.Downed&&!Sleeping(p)&&p.Position.DistanceTo(DevicePosition(d))<=g.RadioRange)State.Guards[g.Id].Receive(new GuardMemory{Identified=true,LastKnown=Cell(Actor.Position),LastSeenTick=tick},tick,g);
                    }
                }
                var uplink=Config.Devices.FirstOrDefault(u=>u.Id==d.Uplink);
                if(d.AutoReport&&s.Identified&&DeviceWorking(uplink)){
                    s.ReportProgress+=Config.UpdateTicks;
                    if(s.ReportProgress>=d.ReportTicks){s.Reported=true;Leak(d.Id);}
                }else s.ReportProgress=0;
            }
        }
        public void Leak(string source)
        {
            if(State.Leaked)return;State.Leaked=true;State.LeakSource=source;Emit("IntelLeaked",source);
        }
        void TickGuard(GuardSpec g,int tick)
        {
            var p=Resolve(g.Pawn) as Pawn;var m=State.Guards[g.Id];
            if(p?.Spawned!=true||p.Dead||p.Downed||Sleeping(p)){m.ReportProgress=0;m.RadioAt=-1;return;}
            if(State.PendingKind=="Takedown"&&State.PendingTarget==p.ThingID){m.ReportProgress=0;return;}
            if(!string.IsNullOrEmpty(m.ScriptedAction))return;
            string oldMode=m.Mode;bool sees=SeesActor(g);
            if(sees)m.Observe(Cell(Actor.Position),tick,Config.UpdateTicks,g,tick-State.PlayerNoiseAt<=Config.UpdateTicks*2,p.Position.DistanceTo(Actor.Position));
            else m.LoseSight(tick,Config.UpdateTicks,g);
            foreach(var body in map.listerThings.AllThings.Where(t=>t is Corpse||t is Pawn bp&&(bp.Downed||Sleeping(bp))).ToList()){
                if(body==p||m.BodiesSeen.Contains(body.ThingID))continue;
                if(GuardCanSee(g,body.Position)){
                    m.BodiesSeen.Add(body.ThingID);m.Alarmed=true;m.Hear(Cell(body.Position),tick,g,true);Emit("BodyFound",g.Id,body.ThingID);
                }
            }
            if(m.RadioAt>=0&&tick>=m.RadioAt){
                foreach(var other in Config.Guards.Where(o=>o.Id!=g.Id&&o.RadioChannel==g.RadioChannel&&g.RadioChannel!="")){
                    var receiver=Resolve(other.Pawn) as Pawn;
                    if(receiver?.Spawned==true&&!receiver.Dead&&!receiver.Downed&&!Sleeping(receiver)&&receiver.Position.DistanceTo(p.Position)<=g.RadioRange)State.Guards[other.Id].Receive(m,tick,other);
                }m.RadioAt=-1;
            }
            var uplink=Config.Devices.FirstOrDefault(d=>d.Id==g.Uplink);
            // Direct contact takes priority over leaving for a transmitter.
            if(m.Identified&&!sees&&!m.Reported&&DeviceWorking(uplink)){
                m.Mode="Reporting";var pos=DevicePosition(uplink);
                bool firstWarning=WarnFirstReport(g,p);
                if(p.Position.DistanceTo(pos)<=1.5f){
                    Wait(p);if(!firstWarning)m.ReportProgress+=Config.UpdateTicks;
                    if(m.ReportProgress>=g.ReportTicks){m.Reported=true;Leak(g.Id);}
                }else{m.ReportProgress=0;Go(p,pos,PathEndMode.Touch,LocomotionUrgency.Jog);}
            }else{
                m.ReportProgress=0;
                if(m.Mode=="Reporting")m.Mode="Search";
                if(m.Mode=="Combat"&&sees)Fight(p);
                else if(m.Mode=="Observing"&&sees){Wait(p);p.Rotation=Rot4.FromAngleFlat((Actor.DrawPos-p.DrawPos).AngleFlat());}
                else if(m.Mode=="Search"||m.Mode=="Investigate"||m.Mode=="Suspicious")Search(p,g,m,tick);
                else Patrol(p,g,m,tick);
            }
            if(State.ReportWarningGuard==g.Id)RefreshVision();
            if(!CanAttackActor(g)&&p.mindState.enemyTarget==Actor){p.mindState.enemyTarget=null;p.stances.CancelBusyStanceSoft();if(p.CurJob?.def==JobDefOf.AttackMelee||p.CurJob?.def==JobDefOf.Wait_Combat)p.jobs.StopAll();}
            if(m.Mode!=oldMode)Emit("GuardState",g.Id,m.Mode);
            var controller=CampaignController.ForMap(map);
            if(m.Identified)controller?.RecordPerformance("detected","guard:"+g.Id);
            if(m.Mode=="Reporting")controller?.RecordPerformance("report","guard:"+g.Id);
        }
        void Go(Pawn p,IntVec3 destination,PathEndMode end=PathEndMode.OnCell,LocomotionUrgency urgency=LocomotionUrgency.Walk)
        {
            if(end==PathEndMode.Touch){
                var adjacent=GenAdj.AdjacentCellsAndInside.Select(o=>destination+o).Where(c=>c.InBounds(p.Map)&&c.Standable(p.Map)&&p.CanReach(c,PathEndMode.OnCell,Danger.Deadly)).OrderBy(c=>c.DistanceToSquared(p.Position)).ToList();
                if(adjacent.Count==0){Wait(p);return;}destination=adjacent[0];
                if(destination==p.Position){Wait(p);return;}
            }
            if(p.CurJob?.def==JobDefOf.Goto&&p.CurJob.targetA.Cell==destination){p.CurJob.locomotionUrgency=urgency;return;}
            if(!p.CanReach(destination,end,Danger.Deadly)){Wait(p);return;}
            var job=JobMaker.MakeJob(JobDefOf.Goto,destination);job.locomotionUrgency=urgency;job.expiryInterval=180;
            job.targetA=new LocalTargetInfo(destination);Give(p,job);
        }
        void Wait(Pawn p)
        {
            if(p.CurJob?.def==JobDefOf.Wait)return;
            Give(p,JobMaker.MakeJob(JobDefOf.Wait,3600));
        }
        void Fight(Pawn p)
        {
            p.mindState.enemyTarget=Actor;var verb=p.TryGetAttackVerb(Actor);
            if(verb==null)return;
            if(verb.verbProps.IsMeleeAttack){if(p.CurJob?.def!=JobDefOf.AttackMelee)Give(p,JobMaker.MakeJob(JobDefOf.AttackMelee,Actor));}
            else if(verb.CanHitTarget(Actor)){if(p.CurJob?.def!=JobDefOf.Wait_Combat)Give(p,JobMaker.MakeJob(JobDefOf.Wait_Combat,90));}
            else Go(p,Actor.Position,PathEndMode.OnCell,LocomotionUrgency.Jog);
        }
        void Patrol(Pawn p,GuardSpec g,GuardMemory m,int tick)
        {
            if(ConversationHolds(p)){Wait(p);return;}
            var route=Config.Routes.FirstOrDefault(r=>r.Id==g.Route);
            if(route==null||route.Points.Count==0){Wait(p);return;}
            m.RouteIndex=Math.Max(0,Math.Min(m.RouteIndex,route.Points.Count-1));var point=route.Points[m.RouteIndex];
            if(p.Position==Position(point.Position)){
                Wait(p);p.Rotation=new Rot4(point.Facing);
                if(m.WaitUntil==0)m.WaitUntil=tick+(point.Checkpoint?point.WaitTicks:0);
                if(tick>=m.WaitUntil)m.NextWaypoint(route);
            }else Go(p,Position(point.Position));
        }
        void Search(Pawn p,GuardSpec g,GuardMemory m,int tick)
        {
            var center=Position(m.Investigate??m.LastKnown);if(!center.IsValid){Wait(p);return;}
            if(m.SearchGoal==null)m.SearchGoal=Cell(center);var goal=Position(m.SearchGoal);
            if(p.Position.DistanceTo(goal)>1.5f){Go(p,goal,PathEndMode.OnCell,m.Identified?LocomotionUrgency.Jog:LocomotionUrgency.Walk);return;}
            Wait(p);p.Rotation=new Rot4((tick/60)%4);
            if(tick%60<Config.UpdateTicks){
                var cells=GenRadial.RadialCellsAround(center,g.SearchRadius,true).Where(c=>c.InBounds(map)&&c.Standable(map)&&p.CanReach(c,PathEndMode.OnCell,Danger.Deadly)).ToList();
                if(cells.Count>0)m.SearchGoal=Cell(cells[(tick/60+Math.Abs(g.Id.GetHashCode()%1000))%cells.Count]);
            }
        }
    }
}
