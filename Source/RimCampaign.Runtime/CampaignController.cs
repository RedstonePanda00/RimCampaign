using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimCampaign.Runtime
{
    public partial class CampaignController : GameComponent, IHost, IConditionHost
    {
        // Runs once on new node map entry, before its checkpoint; never on save reload.
        public static event Action<CampaignController> NodeInitialized;
        public Project Project; public string CampaignId="", NodeId="", Checkpoint="";
        public Map Map; public bool Starting, Transitioning, EntryPending;
        public Dictionary<string,Pawn> Actors=new Dictionary<string,Pawn>();
        public RunState State=new RunState();
        public Dictionary<string,double> CampaignVariables=new Dictionary<string,double>();
        public Dictionary<string,bool> Observed=new Dictionary<string,bool>();
        public Dictionary<string,Thing> Objects=new Dictionary<string,Thing>();
        public HashSet<string> Completed=new HashSet<string>();
        List<Pawn> waitingActors=new List<Pawn>();
        List<string> actorKeys,objectKeys;List<Pawn> actorValues;List<Thing> objectValues;
        Engine engine; double gameDelta; bool failureShown, terminalHandled, checkpointWriting, settling, nativeVictoryPending;
        readonly Queue<string> deferredSaves=new Queue<string>();
        readonly Dictionary<string,object> modules=new Dictionary<string,object>();
        readonly Dictionary<string,float> progress=new Dictionary<string,float>();
        readonly HashSet<string> acknowledgements=new HashSet<string>();
        readonly HashSet<string> openDialogs=new HashSet<string>();
        string subtitle=""; float black;
        bool takingControl; Vector3 savedCamera; float savedSize; TimeSpeed savedSpeed;
        public CampaignController(Game game) { }
        public Campaign Campaign=>Project?.Campaigns.FirstOrDefault(c=>c.Id==CampaignId);
        public Node Node=>Campaign?.Nodes.FirstOrDefault(n=>n.Id==NodeId);
        public MapSpec MapSpec=>Project?.Maps.FirstOrDefault(m=>m.Id==Node?.Map) ?? new MapSpec { Id="RC_Open_"+NodeId,Roles=new List<string>() };
        public bool Active=>Project!=null;
        public bool StoryActive=>Active&&Node?.Kind=="Story";
        static IEnumerable<Act> ActiveActions(Fiber f) {
            if(f.Done)yield break;
            if(f.Steps[f.Index].Kind=="Cinematic")yield return f.Steps[f.Index];
            if(f.Children.Count>0) {foreach(var child in f.Children)foreach(var a in ActiveActions(child))yield return a;}
            else yield return f.Steps[f.Index];
        }
        public bool Presentation=>Active&&(Cinematic || Transitioning || State.Phase=="Failing" || State.Fibers.SelectMany(ActiveActions).Any(a=>new[]{"Fade","Camera","Move","Dialogue","Subtitle","Sound"}.Contains(a.Kind)||a.Freeze));
        public bool Freeze=>Active&&(!MapMission||(State.Phase!="Succeeded"&&State.Phase!="Defeated"))&&(Transitioning || EntryPending || State.Phase=="Failing" || State.Phase=="Defeated" || State.Phase=="Succeeded" || State.Fibers.SelectMany(ActiveActions).Any(a=>a.Freeze));
        public void Prepare(CampaignDef def) {
            Prepare(def.LoadProject(),def.campaignId);
        }
        public void Prepare(Project project,string campaignId,string startNode=null) {
            Project=project;CampaignId=campaignId;NodeId=startNode??Campaign.Start;
            CampaignVariables=new Dictionary<string,double>(Campaign.Variables);
            BindActors(); Starting=true;Transitioning=true;black=1;
        }
        void BindActors() {
            var previous=Actors;Actors=new Dictionary<string,Pawn>();
            foreach(var role in Node.Roles) {
                if(Node.Inherit&&previous.TryGetValue(role.Key,out var pawn))Actors[role.Key]=pawn;
                else Actors[role.Key]=Profiles.Create(Project.Profiles.Single(p=>p.Id==role.Value),Faction.OfPlayer);
            }
            foreach(var role in Node.Roles)foreach(var relation in Project.Profiles.Single(p=>p.Id==role.Value).Relations){
                var target=Node.Roles.FirstOrDefault(x=>x.Value==relation.TargetProfile);
                if(target.Key!=null&&Actors[target.Key]!=Actors[role.Key]){
                    var def=DefDatabase<PawnRelationDef>.GetNamed(relation.Relation);
                    if(!Actors[role.Key].relations.DirectRelationExists(def,Actors[target.Key]))Actors[role.Key].relations.AddDirectRelation(def,Actors[target.Key]);
                }
            }
        }
        public Thing Place(Placement p,Map map) {
            State?.Variables.Remove("destroyed-placement:"+p.Id);
            var cell=new IntVec3(p.Position.X,0,p.Position.Z);
            if(!cell.InBounds(map))throw new InvalidOperationException("Placement out of bounds: "+p.Name);
            if(p.Kind=="Pawn") {
                if(!Actors.TryGetValue(p.Role,out var actor)||actor.Dead)return null;
                if(actor.Spawned)return actor;
                var faction=Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(MapSpec,p.Faction);
                if(actor.Faction!=faction)actor.SetFaction(faction);
                GenSpawn.Spawn(actor,cell,map,new Rot4(p.Rotation));Objects[p.Id]=actor;return actor;
            }
            if(p.Kind=="Terrain"){TerrainSurfaces.Place(map,cell,DefDatabase<TerrainDef>.GetNamed(p.Def.Name));return null;}
            if(p.Kind=="Roof"){map.roofGrid.SetRoof(cell,string.IsNullOrEmpty(p.Def.Name)?null:DefDatabase<RoofDef>.GetNamed(p.Def.Name));return null;}
            if(p.Kind=="Boss") {
                var template=Project.Bosses.Single(b=>b.Id==p.Boss);
                var faction=Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(MapSpec,p.Faction);
                Pawn source=null;
                if(!string.IsNullOrEmpty(p.BindRole)&&!Actors.TryGetValue(p.BindRole,out source))throw new InvalidOperationException("Missing Boss source role: "+p.BindRole);
                var profile=string.IsNullOrEmpty(template.Profile)?null:Project.Profiles.Single(a=>a.Id==template.Profile);
                var boss=map.GetComponent<BossMapComponent>().Spawn(template,cell,faction,p.Id,source,profile);
                Objects[p.Id]=boss;return boss;
            }
            if(p.Kind=="PawnKind") {
                var faction=Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(MapSpec,p.Faction);
                var generated=Profiles.Generate(p.Def,faction);
                GenSpawn.Spawn(generated,cell,map,new Rot4(p.Rotation));Objects[p.Id]=generated;return generated;
            }
            var def=DefDatabase<ThingDef>.GetNamed(p.Def.Name);
            PlacementStorage.Validate(p);
            var thing=CampaignItems.Make(def,PlacementStorage.StuffFor(def,p.Stuff),p.Book);
            PlacementGraphics.Apply(p,thing);
            thing.stackCount=Math.Min(Math.Max(1,p.Count),def.stackLimit);
            if(thing.def.CanHaveFaction)thing.SetFaction(Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(MapSpec,p.Faction));
            GenSpawn.Spawn(thing,cell,map,new Rot4(p.Rotation));PlacementStorage.Populate(thing,p);if(thing is Plant plant&&p.Layer==TerrainInitializer.NatureLayer)plant.Growth=.75f+(float)TerrainNoise.Sample(cell.x,cell.z,37)*.25f;Objects[p.Id]=thing;return thing;
        }
        void Populate() {
            foreach(var role in Actors) {
                var p=MapSpec.Objects.FirstOrDefault(o=>o.Kind=="Pawn"&&o.Role==role.Key);
                if(p!=null&&!p.SpawnAtStart){DetachActor(role.Value);continue;}
                if(p!=null){
                    Objects[p.Id]=role.Value;
                    var faction=Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(MapSpec,p.Faction);
                    if(role.Value.Faction!=faction)role.Value.SetFaction(faction);
                }
                var at=p==null?Map.Center:new IntVec3(p.Position.X,0,p.Position.Z);
                if(Node.Kind=="Open")at=OpenArrivalCell(at,Map);
                if(role.Value.Dead){
                    var corpse=role.Value.Corpse;
                    if(corpse!=null){if(corpse.Spawned)corpse.DeSpawn();GenSpawn.Spawn(corpse,at,Map);}continue;
                }
                DetachActor(role.Value);
                if(Node.Arrival=="Pods")DropPodUtility.DropThingsNear(at,Map,new List<Thing>{role.Value},110,false,false,false);
                else {
                    var start=Node.Arrival=="Edge"?new IntVec3(1,0,Mathf.Clamp(at.z,1,Map.Size.z-2)):at;
                    if(Node.Arrival!="Position"&&Node.Arrival!="Edge") {
                        var extension=Modules.Create(Node.Arrival) as IArrivalModule;
                        if(extension==null)throw new InvalidOperationException("Unknown arrival "+Node.Arrival);
                        extension.Arrive(role.Value,Map,new Cell{X=at.x,Z=at.z});
                    } else {
                        GenSpawn.Spawn(role.Value,start,Map);
                        if(Node.Arrival=="Edge")role.Value.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto,at),JobCondition.InterruptForced);
                    }
                }
            }
            foreach(var p in Node.Resources.Where(p=>p.SpawnAtStart))Place(p,Map);
            Map.Parent.SetFaction(Faction.OfPlayer);
            Current.Game.CurrentMap=Map;Find.World.renderer.wantedMode=WorldRenderMode.None;
            Find.CameraDriver.JumpToCurrentMapLoc(Map.Center);Find.CameraDriver.ResetSize();
            GenerateMapRewards();
            CampaignMapModules.Apply(this);
        }
        internal static IntVec3 OpenArrivalCell(IntVec3 desired,Map map) {
            if(!desired.InBounds(map))desired=map.Center;
            var near=CellFinder.StandableCellNear(desired,map,12);
            if(near.InBounds(map)&&near.Standable(map))return near;
            // Mountain starts can contain no walkable cell in the local search radius.
            var best=IntVec3.Invalid;int distance=int.MaxValue;
            foreach(var cell in map.AllCells)if(cell.Standable(map)) {
                int candidate=(cell-desired).LengthHorizontalSquared;
                if(candidate<distance){best=cell;distance=candidate;}
            }
            if(!best.IsValid)throw new InvalidOperationException("No standable arrival cell on generated map.");
            return best;
        }
        public override void StartedNewGame() {
            if(!Active)return;Starting=false;
            if(Node.Kind=="Open")Map=Find.CurrentMap;
            Populate();
            // Vanilla initialization creates a starting settlement; the campaign starts in its pocket map.
            foreach(var settlement in Find.WorldObjects.Settlements.Where(s=>s.Faction==Faction.OfPlayer&&!s.HasMap).ToList())Find.WorldObjects.Remove(settlement);
            if(Node.Kind=="Open")NodeInitialized?.Invoke(this);
            LongEventHandler.ExecuteWhenFinished(CreateCheckpoint);
        }
        void CreateCheckpoint() {
            try {
                RestoreControl();State=new RunState();Result=null;EndingText="";Observed.Clear();engine=new Engine(MapSpec,State,this);
                Communications.Messages.Clear();progress.Clear();openDialogs.Clear();acknowledgements.Clear();failureShown=terminalHandled=nativeVictoryPending=settling=false;gameDelta=0;
                EntryPending=true;Transitioning=false;Checkpoint="RimCampaign-"+Guid.NewGuid().ToString("N");
                checkpointWriting=true;
                try{GameDataSaveLoader.SaveGame(Checkpoint);}finally{checkpointWriting=false;}
                var path=GenFilePaths.FilePathForSavedGame(Checkpoint);
                if(!File.Exists(path)||new FileInfo(path).Length==0)throw new IOException("Entry checkpoint could not be saved.");
                StartEntry();
            }catch(Exception ex){StopWithError(ex);}
        }
        void StartEntry() {
            // A result/failure window pauses the previous node. Do not inherit that pause
            // into this node's cinematic control snapshot or first spatial-trigger tick.
            Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
            EntryPending=false;Transitioning=false;black=0;engine=new Engine(MapSpec,State,this);engine.Start();
            foreach(var input in Node.Inputs)State.Variables[input.Key]=input.Value;
            if(Node.OpeningTitle?.ManualStart!=true)BeginOpeningTitle();
        }
        public override void LoadedGame() {
            if(!Active)return;engine=new Engine(MapSpec,State,this);
            if(EntryPending)LongEventHandler.ExecuteWhenFinished(StartEntry);
            else if(Transitioning)LongEventHandler.ExecuteWhenFinished(BeginTransition);
        }
        public override void ExposeData() {
            // Destroyed objects are not written as live Thing references. Preserve
            // their result so a partially cleared encounter remains clear on load.
            if(Scribe.mode==LoadSaveMode.Saving)
                foreach(var pair in Objects)if(pair.Value?.Destroyed==true||pair.Value is Pawn dead&&dead.Dead)State.Variables["destroyed-placement:"+pair.Key]=1;
            Scribe_Collections.Look(ref RewardStocks,"rcMapRewardStocks",LookMode.Deep);
            ExposeStory();
            ExposeResult();
            ExposeCommunications();
            ExposeOpeningTitle();
            string project=Project==null?null:Json.Write(Project),state=Json.Write(State),variables=Json.Write(CampaignVariables),observed=Json.Write(Observed);
            ScribeJson.Look(ref project,"rcProject");ScribeJson.Look(ref state,"rcState");
            ScribeJson.Look(ref variables,"rcVariables");ScribeJson.Look(ref observed,"rcObserved");
            Scribe_Values.Look(ref CampaignId,"rcCampaign");Scribe_Values.Look(ref NodeId,"rcNode");Scribe_Values.Look(ref Checkpoint,"rcCheckpoint");
            Scribe_Values.Look(ref EntryPending,"rcEntryPending");Scribe_Values.Look(ref Transitioning,"rcTransitioning");
            Scribe_Values.Look(ref nativeVictoryPending,"rcNativeVictoryPending");
            Scribe_References.Look(ref Map,"rcMap");
            if(Scribe.mode==LoadSaveMode.Saving)waitingActors=Actors.Values.Where(p=>p!=null&&!p.Spawned&&!p.Dead&&p.ParentHolder==null&&!Find.WorldPawns.Contains(p)).Distinct().ToList();
            Scribe_Collections.Look(ref waitingActors,"rcWaitingActors",LookMode.Deep);
            Scribe_Collections.Look(ref Actors,"rcActors",LookMode.Value,LookMode.Reference,ref actorKeys,ref actorValues);
            Scribe_Collections.Look(ref Objects,"rcObjects",LookMode.Value,LookMode.Reference,ref objectKeys,ref objectValues);
            Scribe_Collections.Look(ref Completed,"rcCompleted",LookMode.Value);
            if(Scribe.mode==LoadSaveMode.LoadingVars) {
                Project=project==null?null:Json.Read<Project>(project);
                State=state==null?new RunState():Json.Read<RunState>(state);
                CampaignVariables=variables==null?new Dictionary<string,double>():Json.Read<Dictionary<string,double>>(variables);
                Observed=observed==null?new Dictionary<string,bool>():Json.Read<Dictionary<string,bool>>(observed);
            }
        }
        public override void GameComponentTick() {
            if(!Active||Transitioning||EntryPending)return;
            gameDelta+=1d/60;
            if(Cinematic)return;
            PollTriggers();
            foreach(var descriptor in Modules.All.Where(d=>typeof(IEventModule).IsAssignableFrom(d.Type))){
                if(!modules.TryGetValue(descriptor.Id,out var module))modules[descriptor.Id]=module=Modules.Create(descriptor.Id);
                foreach(var signal in ((IEventModule)module).Poll(this,1d/60))engine?.Emit(signal.Kind,signal.Target,signal.Source);
            }
            foreach(var pair in Actors) {
                Change("Death",pair.Key,pair.Value.Dead);
                Change("Downed",pair.Key,pair.Value.Downed&&!pair.Value.Dead);
                foreach(var r in MapSpec.Regions) {
                    bool inside=pair.Value.Spawned&&pair.Value.Map==Map&&pair.Value.Position.x>=r.Min.X&&pair.Value.Position.x<=r.Max.X&&pair.Value.Position.z>=r.Min.Z&&pair.Value.Position.z<=r.Max.Z;
                    string key="region:"+r.Id+":"+pair.Key;
                    bool old=Observed.TryGetValue(key,out var was)&&was;
                    if(inside!=old)engine?.Emit(inside?"Enter":"Leave",r.Id,pair.Key);
                    Observed[key]=inside;
                }
            }
        }
        void Change(string kind,string role,bool value) {
            string key=kind+":"+role;bool old=Observed.TryGetValue(key,out var v)&&v;
            if(value&&!old)engine?.Emit(kind,role,role);Observed[key]=value;
        }
        public override void GameComponentUpdate() {
            if(!Active||engine==null||Transitioning||EntryPending)return;
            try {
                // Anomaly returns its pawns several ticks after showing credits. Let that
                // native transfer finish before removing maps or starting the next node.
                if(nativeVictoryPending&&(Find.Anomaly==null||Find.Anomaly.metalHellPawns.Count==0)) {
                    nativeVictoryPending=false;State.WantSuccess=true;
                    ScreenFader.SetColor(Color.clear);Find.MusicManagerPlay.ForceSilenceFor(0);
                }
                communicationPresentationDelta=Math.Min(Time.unscaledDeltaTime,0.1);
                PruneCommunications();engine.Tick(gameDelta,communicationPresentationDelta);gameDelta=0;PruneCommunications();
                if(Presentation&&!takingControl&&(!MapMission||Find.CurrentMap==Map)) TakeControl();
                if(!Presentation&&!nativeVictoryPending) {RestoreControl();if(deferredSaves.Count>0)GameDataSaveLoader.SaveGame(deferredSaves.Dequeue());}
                if((State.Phase=="Succeeded"||State.Phase=="Defeated")&&!terminalHandled) {
                    terminalHandled=true;RestoreControl();
                    if(FinishMapMission())return;
                    RefillCompletedMissionNeeds();
                    var edge=engine.Next(Campaign,NodeId,State.Phase=="Succeeded"?"success":"failure");
                    if(State.Phase=="Succeeded"&&Node.Results!=null)ShowResult();
                    else if(edge!=null)Transition(edge.To);
                    else if(State.Phase=="Defeated")ShowDefeat();
                    else {Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Find.WindowStack.Add(new Dialog_MessageBox(string.IsNullOrEmpty(EndingText)?RimCampaign.Runtime.RcText.T("战役完成 / Campaign complete"):RcText.T(EndingText))); }
                }
            }catch(Exception ex){StopWithError(ex);}
        }
        public void RestoreControl() {
            if(takingControl) {if(!keepPresentationCamera)Find.CameraDriver.SetRootPosAndSize(savedCamera,savedSize);Find.TickManager.CurTimeSpeed=savedSpeed;takingControl=false;}
            subtitle="";black=0;EndCinematic();
        }
        void TakeControl() {
            if(takingControl)return;
            takingControl=true;savedCamera=Find.CameraDriver.MapPosition.ToVector3();savedSize=Find.CameraDriver.RootSize;savedSpeed=Find.TickManager.CurTimeSpeed;Find.Selector.ClearSelection();
            // Cinematics skip MainTabsRoot's normal empty-selection cleanup, but still draw
            // modal windows. Close the inspect window itself so its empty background cannot linger.
            if(Find.MainTabsRoot.OpenTab==MainButtonDefOf.Inspect)Find.MainTabsRoot.SetCurrentTab(null,false);
        }
        void StopWithError(Exception ex) {
            Log.Error("[RimCampaign] "+ex);RestoreControl();Transitioning=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
            Find.WindowStack.Add(new Dialog_MessageBox("RimCampaign: "+ex.Message));
        }
        public bool DeferSave(string name) {
            if(!Active||checkpointWriting||(!Presentation&&!EntryPending&&!nativeVictoryPending))return false;
            if(!deferredSaves.Contains(name))deferredSaves.Enqueue(name);return true;
        }
        void ShowDefeat() {if(failureShown)return;failureShown=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Find.WindowStack.Add(new DefeatWindow(this));}
        public void Retry() {RestoreControl();GameDataSaveLoader.LoadGame(Checkpoint);}
        public bool NativeVictory() {
            if(!Active||Node.Kind!="Open")return false;
            if(State.Phase!="Running"||Transitioning||nativeVictoryPending)return true;
            // ShipCountdown destroys the ship immediately after ShowCredits returns.
            // Release campaign actors while their container and map are still valid.
            foreach(var pawn in Actors.Values.Distinct()) {
                if(pawn.Dead||!(pawn.ParentHolder is Building_CryptosleepCasket casket)||!casket.Spawned)continue;
                var map=casket.Map;var cell=CellFinder.StandableCellNear(casket.Position,map,12);
                DetachActor(pawn);GenSpawn.Spawn(pawn,cell,map);
            }
            nativeVictoryPending=true;
            ScreenFader.SetColor(Color.clear);
            return true;
        }
        static void DetachActor(Pawn pawn) {
            if(pawn.Spawned)pawn.DeSpawn();
            pawn.holdingOwner?.Remove(pawn);
            if(Find.WorldPawns.Contains(pawn))Find.WorldPawns.RemovePawn(pawn);
            pawn.jobs?.StopAll();
        }
        public void Transition(string next) {
            if(next==NodeId||Completed.Contains(next))throw new InvalidOperationException("Cannot re-enter completed node.");
            if(!Campaign.Nodes.Any(n=>n.Id==next))throw new InvalidOperationException("Missing campaign node: "+next);
            foreach(var letter in Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_StoryChoice>().Where(l=>l.NodeId==NodeId).ToList())Find.LetterStack.RemoveLetter(letter);
            Completed.Add(NodeId);NodeId=next;Transitioning=true;RestoreControl();
            BeginTransition();
        }
        void BeginTransition() {
            if(Node.Kind=="Open") {Find.World.renderer.wantedMode=WorldRenderMode.Planet;Find.WindowStack.Add(new OpenNodeWindow(this));}
            else LongEventHandler.QueueLongEvent(()=>EnterMap(null),"GeneratingMap",false,null);
        }
        public bool TryChooseOpenTile(PlanetTile tile) {
            if(!Active||!Transitioning||Node.Kind!="Open"||settling||!tile.Valid||!TileFinder.IsValidTileForNewSettlement(tile))return false;
            var settlement=(Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            settlement.Tile=tile;settlement.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(settlement);
            settling=true;
            LongEventHandler.QueueLongEvent(()=>EnterMap(settlement),"GeneratingMap",false,null);
            return true;
        }
        public void EnterMap(Settlement settlement) {
            try {
                var old=Map;
                if(old!=null)foreach(var module in old.components.OfType<ICampaignMapModule>().ToList())module.Detach();
                Objects.Clear();taskPawns.Clear();
                Map=settlement==null
                    ?CampaignMaps.Generate(MapSpec,old?.Tile??Find.GameInfo.startingTile)
                    :MapGenerator.GenerateMap(new IntVec3(250,1,250),settlement,settlement.MapGeneratorDef,settlement.ExtraGenStepDefs);
                BindActors();
                Populate();
                NodeInitialized?.Invoke(this);
                if(old!=null&&old!=Map){
                    var parent=old.Parent;
                    if(old.IsPocketMap)PocketMapUtility.DestroyPocketMap(old);else {Current.Game.DeinitAndRemoveMap(old,false);Find.WorldObjects.Remove(parent);}
                }
                LongEventHandler.ExecuteWhenFinished(CreateCheckpoint);
            }catch(Exception ex){StopWithError(ex);}
        }
        public double Read(string key) {
            if(key.StartsWith("campaign."))return CampaignVariables.TryGetValue(key.Substring(9),out var n)?n:0;
            var parts=key.Split(':');
            if(parts[0]=="boss"&&parts.Length>2) {
                var boss=Map?.GetComponent<BossMapComponent>()?.Records.LastOrDefault(b=>b.Id==parts[1]);
                if(boss==null)return 0;
                return parts[2]=="health"?boss.Health:parts[2]=="max"?boss.Template.MaxHealth:parts[2]=="phase"?boss.Phase+1:parts[2]=="ended"&&boss.Ended?1:0;
            }
            if(parts[0]=="elapsed")return State.Elapsed;
            if(parts[0]=="timer")return parts.Length>1&&State.Timers.TryGetValue(parts[1],out var t)?t:0;
            if(parts[0]=="dead")return parts.Length>1&&Actors.TryGetValue(parts[1],out var pawn)&&pawn.Dead?1:0;
            if(parts[0]=="group"&&parts.Length>1)return MapSpec.Objects.Where(o=>o.Group==parts[1]).Count(o=>Objects.TryGetValue(o.Id,out var obj)&&!obj.Destroyed&&(!(obj is Pawn p)||!p.Dead));
            if(parts[0]=="inventory"&&parts.Length>2&&Actors.TryGetValue(parts[1],out var owner))return owner.inventory.innerContainer.Where(x=>x.def.defName==parts[2]).Sum(x=>x.stackCount);
            if(parts[0]=="region"&&parts.Length>1) {
                var r=MapSpec.Regions.FirstOrDefault(x=>x.Id==parts[1]);if(r==null)return 0;
                return Map.mapPawns.AllPawnsSpawned.Count(p=>p.Position.x>=r.Min.X&&p.Position.x<=r.Max.X&&p.Position.z>=r.Min.Z&&p.Position.z<=r.Max.Z);
            }
            var extension=Modules.Create(parts[0]) as IDataSourceModule;return extension?.Read(key,this)??0;
        }
        public void EmitBossSignal(Map owner,string kind,string id){
            if(!Active||Map!=owner||Transitioning||EntryPending)return;
            foreach(var rule in MapSpec.Rules.Where(r=>r.Host==id&&TriggerEnabled(r)))engine?.EmitForRule(rule.Id,kind,id);
        }
        public bool Evaluate(Condition condition){
            if(!modules.TryGetValue(condition.Op,out var module))modules[condition.Op]=module=Modules.Create(condition.Op);
            if(module is IConditionModule evaluator)return evaluator.Evaluate(condition,this);
            throw new InvalidOperationException("Unknown condition "+condition.Op);
        }
        public bool TestCondition(Condition condition)=>engine?.Test(condition)==true;
        public void Signal(string kind,string target,string source=""){if(Active&&!Transitioning&&!EntryPending)engine?.Emit(kind,target,source);}
        public bool Execute(Act a,string source,double dt) {
            if(MapMission&&Find.CurrentMap!=Map&&(a.Freeze||new[]{"Cinematic","Fade","Camera","Move","Dialogue","Subtitle","Communication","PasswordPrompt"}.Contains(a.Kind)))return false;
            if(new[]{"Fade","Camera","Move","Dialogue","Subtitle"}.Contains(a.Kind))TakeControl();
            string target=a.Target=="$source"?source:a.Target;
            switch(a.Kind) {
                // A queued frozen action may pause gameplay while a non-frozen line is at the
                // front. Keep that line advancing on the presentation clock to avoid deadlock.
                case "Communication":return Communications.Execute(a,source,Freeze?Math.Max(dt,communicationPresentationDelta):dt);
                case "SoundPulse":PlaySoundPulse(target,a.SoundPulse,a.Steps,a.Text);return true;
                case "Noise":
                    var noisePoint=MapSpec.Objects.FirstOrDefault(o=>o.Id==target);
                    if(noisePoint!=null)StealthMapComponent.For(Map)?.EmitNoise(new IntVec3(noisePoint.Position.X,0,noisePoint.Position.Z),(float)a.Value,a.Text=="danger");
                    return true;
                case "PasswordPrompt":
                    Find.WindowStack.Add(new PasswordWindow(a.Text==""?RimCampaign.Runtime.RcText.T("输入密码"):RcText.T(a.Text),input=>SubmitPasswordDetailed(target,input,source)));return true;
                case "UnlockBoss":
                case "BossPhaseComplete":
                    var record=Map.GetComponent<BossMapComponent>().Records.LastOrDefault(b=>b.Id==target&&!b.Ended);
                    if(record!=null&&(a.Kind=="UnlockBoss"||(record.Phase==(int)a.Value&&record.Proxy.ThingID==a.Text)))record.HealthLocked=false;
                    return true;
                case "CampaignSet":CampaignVariables[target]=a.Value;return true;
                case "Spawn":Place(MapSpec.Objects.Single(o=>o.Id==target),Map);return true;
                case "SpawnGroup":
                    foreach(var placement in MapSpec.Objects.Where(o=>o.Group==target&&o.Kind!="Marker"))Place(placement,Map);return true;
                case "Destroy":
                    var victim=ResolveTriggerPawn(target) as Thing;
                    if(victim==null)Objects.TryGetValue(target,out victim);
                    if(victim is Pawn pawn){if(!pawn.Dead)pawn.Kill(null);}
                    else if(victim!=null&&!victim.Destroyed)victim.Destroy(DestroyMode.KillFinalize);
                    return true;
                case "RemoveTerrain":
                    foreach(var cell in TriggerCells(target))if(cell.InBounds(Map))Map.terrainGrid.RemoveTopLayer(cell,false);
                    return true;
                case "Despawn":if(Objects.TryGetValue(target,out var obj)&&obj.Spawned)obj.DeSpawn();return true;
                case "Sound":DefDatabase<SoundDef>.GetNamed(target).PlayOneShotOnCamera();return true;
                case "Subtitle":subtitle=RcText.T(a.Text);return Timed(a,dt);
                case "Fade":black=Mathf.Lerp(a.Text=="in"?1:0,a.Text=="in"?0:1,Progress(a,dt));return progress[a.Id]>=Math.Max(0.001,a.Value);
                case "Cinematic":BeginCinematic(a);return true;
                case "Camera":return MoveCamera(a,dt);
                case "Move":
                    var actor=ResolveTriggerPawn(target)??throw new InvalidOperationException("Missing moving Pawn: "+target);var point=MapSpec.Objects.Single(o=>o.Id==a.Text);var destination=new IntVec3(point.Position.X,0,point.Position.Z);
                    var stealth=StealthMapComponent.For(actor);var guard=stealth?.Guard(actor);
                    // An incapacitated actor cannot finish walking. End its branch so a parallel scene can continue.
                    if(actor.Dead||actor.Downed||!actor.Spawned||actor.Map!=Map){
                        if(guard!=null)stealth.State.Guards[guard.Id].ScriptedAction="";
                        return true;
                    }
                    if(a.Freeze) {
                        var end=Progress(a,dt)>=1;if(end){actor.Position=destination;actor.Drawer.tweener.ResetTweenedPosToRoot();}return end;
                    }
                    if(actor.Position==destination&&!actor.pather.Moving){
                        if(guard!=null)stealth.State.Guards[guard.Id].ScriptedAction="";
                        return true;
                    }
                    if(!progress.ContainsKey(a.Id)||actor.CurJob?.def!=JobDefOf.Goto||actor.CurJob.targetA.Cell!=destination){
                        if(guard!=null)stealth.State.Guards[guard.Id].ScriptedAction=a.Id;
                        var move=JobMaker.MakeJob(JobDefOf.Goto,destination);move.playerForced=true;
                        actor.jobs.StartJob(move,JobCondition.InterruptForced);progress[a.Id]=0;
                    }
                    bool arrived=actor.Position==destination&&!actor.pather.Moving;
                    if(arrived&&guard!=null)stealth.State.Guards[guard.Id].ScriptedAction="";
                    return arrived;
                case "Dialogue":
                    if(acknowledgements.Contains(a.Id))return true;
                    if(openDialogs.Add(a.Id))Find.WindowStack.Add(new DialogueWindow(a,()=>acknowledgements.Add(a.Id),choice=>engine.Enqueue(choice.Steps,State.Phase=="Failing")));
                    return false;
                case "Message":Messages.Message(RcText.T(a.Text),MessageTypeDefOf.NeutralEvent,false);return true;
                default:
                    if(!modules.TryGetValue(a.Kind,out var module))modules[a.Kind]=module=Modules.Create(a.Kind);
                    if(module is IActionModule handler)return handler.Execute(a,this,source,dt);
                    throw new InvalidOperationException("Unknown action "+a.Kind);
            }
        }
        float Progress(Act a,double dt) {if(!progress.ContainsKey(a.Id))progress[a.Id]=0;progress[a.Id]+=(float)dt;return Mathf.Clamp01(progress[a.Id]/(float)Math.Max(0.001,a.Value));}
        bool Timed(Act a,double dt)=>Progress(a,dt)>=1;
        public override void GameComponentOnGUI() {
            // Entry also draws game-component GUI. Keep the prepared campaign's
            // landing fade off the native planet/site-selection screen.
            if(!Active||Starting||(!MapMission&&Find.CurrentMap?.GetComponent<MissionMapComponent>()?.Controller!=null))return;
            if(!Transitioning&&!EntryPending&&Map!=null&&Find.CurrentMap==Map)
                foreach(var overlay in Map.components.OfType<ICampaignMapOverlay>())overlay.DrawCampaignOverlay();
            foreach(var w in MapSpec.Hud.Where(w=>!Cinematic)) {
                var rect=new Rect(w.X,w.Y,w.Width,w.Height);
                if(w.Kind=="Progress")Widgets.FillableBar(rect,(float)(Read(w.Binding)/Math.Max(1,w.Maximum)));
                else if(w.Kind=="Objectives") {
                    foreach(var o in engine?.VisibleObjectives()??Enumerable.Empty<Objective>()) {
                        string state=State.Objectives.TryGetValue(o.Id,out var v)?v:"Active";
                        GUI.color=state=="Failed"?Color.red:state=="Complete"?Color.green:Color.white;
                        Widgets.Label(rect,(state=="Failed"?"✗ ":state=="Complete"?"✓ ":"• ")+RcText.T(o.Name));rect.y+=28;
                    }GUI.color=Color.white;
                }else Widgets.Label(rect,w.Kind=="Text"?RcText.T(w.Text):RcText.T(w.Text)+" "+Read(w.Binding).ToString("0"));
            }
            if(black>0)Widgets.DrawBoxSolid(new Rect(0,0,UI.screenWidth,UI.screenHeight),new Color(0,0,0,black));
            if(subtitle!="")Widgets.Label(new Rect(UI.screenWidth*0.15f,UI.screenHeight-120,UI.screenWidth*0.7f,90),subtitle);
            DrawOpeningTitle();
            DrawCommunications();
        }
    }
    public class DefeatWindow : Window
    {
        readonly CampaignController c; public override Vector2 InitialSize=>new Vector2(440,260);
        public DefeatWindow(CampaignController c) {this.c=c;forcePause=true;absorbInputAroundWindow=true;closeOnCancel=false;}
        public override void DoWindowContents(Rect r) {
            Widgets.Label(new Rect(0,0,r.width,40),string.IsNullOrEmpty(c.State.FailureReason)?RimCampaign.Runtime.RcText.T("战败 / Defeat"):RcText.T(c.State.FailureReason));
            if(Widgets.ButtonText(new Rect(0,55,r.width,40),RimCampaign.Runtime.RcText.T("重试本关 / Retry level"))){Close();c.Retry();}
            if(Widgets.ButtonText(new Rect(0,105,r.width,40),RimCampaign.Runtime.RcText.T("读取存档 / Load game")))Find.WindowStack.Add(new Dialog_SaveFileList_Load());
            if(Widgets.ButtonText(new Rect(0,155,r.width,40),RimCampaign.Runtime.RcText.T("回到主界面 / Main menu"))){c.RestoreControl();GenScene.GoToMainMenu();}
        }
    }
    public class DialogueWindow : Window
    {
        readonly Act action;readonly Action done;readonly Action<Act> choose;
        public override Vector2 InitialSize=>new Vector2(650,400);
        public DialogueWindow(Act action,Action done,Action<Act> choose){this.action=action;this.done=done;this.choose=choose;forcePause=action.Freeze;absorbInputAroundWindow=true;closeOnCancel=false;}
        public override void DoWindowContents(Rect r) {
            Widgets.Label(new Rect(0,0,r.width,r.height-150),RcText.T(action.Text));
            var choices=action.Steps.Count==0?new List<Act>{new Act{Name=RimCampaign.Runtime.RcText.T("继续 / Continue")}}:action.Steps;
            float y=r.height-choices.Count*40;
            foreach(var choice in choices){if(Widgets.ButtonText(new Rect(0,y,r.width,34),RcText.T(choice.Name))){choose(choice);done();Close();}y+=40;}
        }
    }
    public class OpenNodeWindow : Window
    {
        readonly CampaignController c;public override Vector2 InitialSize=>new Vector2(420,160);
        public OpenNodeWindow(CampaignController c){this.c=c;forcePause=true;closeOnCancel=false;preventCameraMotion=false;absorbInputAroundWindow=false;}
        public override void DoWindowContents(Rect r){
            Widgets.Label(new Rect(0,0,r.width,50),RimCampaign.Runtime.RcText.T("在世界地图选择殖民地位置 / Select a world tile"));
            if(Widgets.ButtonText(new Rect(0,65,r.width,40),RimCampaign.Runtime.RcText.T("在所选地块定居 / Settle"))) {
                var tile=Find.WorldInterface.SelectedTile;
                if(!c.TryChooseOpenTile(tile)){Messages.Message("Invalid settlement tile",MessageTypeDefOf.RejectInput,false);return;}
                Close();
            }
        }
    }
    [HarmonyPatch(typeof(TickManager),nameof(TickManager.DoSingleTick))]
    static class FreezeSimulation {static bool Prefix()=>CampaignController.Focused?.Freeze!=true;}
    [HarmonyPatch(typeof(GameDataSaveLoader),nameof(GameDataSaveLoader.SaveGame))]
    static class SafeSave {static bool Prefix(string fileName)=>CampaignController.Focused?.DeferSave(fileName)!=true;}
    [HarmonyPatch(typeof(GameVictoryUtility),nameof(GameVictoryUtility.ShowCredits))]
    static class VanillaVictory {
        static bool Prefix()=>Current.Game?.GetComponent<CampaignController>()?.NativeVictory()!=true;
    }
    [HarmonyPatch(typeof(GameEnder),nameof(GameEnder.CheckOrUpdateGameOver))]
    static class CampaignGameOver {static bool Prefix()=>Current.Game?.GetComponent<CampaignController>()?.StoryActive!=true;}
}
