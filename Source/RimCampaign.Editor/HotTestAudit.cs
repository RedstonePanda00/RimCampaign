using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimCampaign.Editor {
    // Explicit command-line native regression only. Never activates during normal play.
    [HarmonyPatch(typeof(MainMenuDrawer),nameof(MainMenuDrawer.DoMainMenuControls))]
    static class HotTestAudit {
        internal static int Stage,Frames;
        internal static Workspace Workspace;
        internal static string RewardId,PendingId;
        static float centerX,centerZ;
        internal static string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"HotTestAudit");
        internal static bool Enabled=>GenCommandLine.CommandLineArgPassed("rc-hot-test-audit");
        internal static void Check(bool ok,string name){Directory.CreateDirectory(Folder);File.AppendAllText(Path.Combine(Folder,"results.txt"),(ok?"PASS ":"FAIL ")+name+"\n");if(!ok)throw new Exception(name);}
        internal static void Fail(Exception ex){Directory.CreateDirectory(Folder);File.AppendAllText(Path.Combine(Folder,"results.txt"),"FAIL "+ex);File.WriteAllText(Path.Combine(Folder,"complete.txt"),"FAIL");Stage=99;Application.Quit();}
        static void Postfix()=>Tick();
        static void ExportAnimalReference(string name,string texturePath){
            var texture=ContentFinder<Texture2D>.Get(texturePath);
            var target=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;Texture2D copy=null;
            try{Graphics.Blit(texture,target);RenderTexture.active=target;copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);copy.Apply();File.WriteAllBytes(Path.Combine(Folder,name+"-vanilla-reference.png"),copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(copy!=null)UnityEngine.Object.Destroy(copy);}
        }
        internal static void Tick(){
            if(!Enabled||Current.ProgramState!=ProgramState.Entry||Stage==99)return;
            try{
                if(Stage==0){
                    Directory.CreateDirectory(Folder);
                    var language=LanguageDatabase.activeLanguage;
                    File.WriteAllLines(Path.Combine(Folder,"translation-errors.txt"),language.loadErrors.Concat(language.defInjections.SelectMany(d=>d.loadErrors)));
                    bool english=language.folderName.StartsWith("English",StringComparison.OrdinalIgnoreCase);
                    Check(RcText.T("保存")== (english?"Save":"保存"),"editor labels use the selected language");
                    Check(RcText.T("梅森")== (english?"Mason":"梅森"),"campaign text uses the selected language");
                    Check(RcText.T("unregistered author text")=="unregistered author text","untranslated custom author text remains unchanged");
                    ExportAnimalReference("wolf","Things/Pawn/Animal/Wolf_Timber/Wolf_Timber_east");
                    ExportAnimalReference("dog","Things/Pawn/Animal/Husky/Husky_east");
                    var reset=new MapSpec{Width=24,Height=24};reset.Components.Add(new MapComponentSpec{Type=typeof(CombatPatrolMapComponent).FullName});reset.PatrolRoutes.Add(new PatrolRoute());reset.CombatPatrols.Add(new CombatPatrolSpec());reset.Rewards.Add(new MapRewardSpec());
                    TerrainInitializer.Initialize(reset,new TerrainInitialization());Check(reset.Components.Count==0&&reset.PatrolRoutes.Count==0&&reset.CombatPatrols.Count==0&&reset.Rewards.Count==0,"full terrain reset clears module, patrol and generation bindings");
                    var input=Environment.GetCommandLineArgs().First(a=>a.StartsWith("-rc-audit-project=")).Substring("-rc-audit-project=".Length);
                    if(GenCommandLine.CommandLineArgPassed("rc-loading-audit")){
                        var copy=Path.Combine(Folder,"authoring.project.json.gz");File.Copy(input,copy);
                        Workspace=new Workspace();Find.WindowStack.Add(Workspace);
                        Traverse.Create(Workspace).Method("OpenProject",copy).GetValue();Stage=10;return;
                    }
                    var project=ProjectFiles.Load(input);Workspace=new Workspace();
                    var t=Traverse.Create(Workspace);t.Field("history").SetValue(new History<Project>(project));t.Field("mapId").SetValue("romulus-outpost");
                    var path=Path.Combine(Folder,"authoring.project.json.gz");ProjectFiles.Save(path,project);t.Field("path").SetValue(path);
                    Check(Workspace.Map.Components.All(s=>s.Type!=typeof(StealthMapComponent).FullName),"mission two explicitly omits stealth");
                    Workspace.Map.Objects.Add(new Placement{Id="unsaved-hot-probe",Kind="Marker",Name="unsaved hot-test edit",Position=new Cell{X=8,Z=8}});
                    Workspace.Map.Rewards.Add(new MapRewardSpec{Id="probe-reward",Name="generation probe",Contents=new List<DeviceLoot>{new DeviceLoot{Def=new DefRef{Name="TextBook"},Book=new BookText{Title="Reward identity probe",Text="Generated before module attachment."}}}});
                    Workspace.Map.Rewards.Add(new MapRewardSpec{Id="probe-pending",Name="pending probe",Contents=new List<DeviceLoot>{new DeviceLoot{Def=new DefRef{Name="MedicineIndustrial"},Count=2}}});
                    Find.WindowStack.Add(Workspace);Stage=1;Frames=0;return;
                }
                if(Stage==10){
                    if(LongEventHandler.AnyEventNowOrWaiting)return;
                    Check(Workspace.Project.Maps.Any(m=>m.Id=="romulus-outpost"),"queued project loading completes before editor resumes");
                    var t=Traverse.Create(Workspace);t.Field("mapId").SetValue("romulus-outpost");
                    Workspace.Map.Objects.Add(new Placement{Id="unsaved-hot-probe",Kind="Marker",Name="unsaved hot-test edit",Position=new Cell{X=8,Z=8}});
                    Workspace.Map.Rewards.Add(new MapRewardSpec{Id="probe-reward",Name="generation probe",Contents=new List<DeviceLoot>{new DeviceLoot{Def=new DefRef{Name="TextBook"},Book=new BookText{Title="Reward identity probe",Text="Generated before module attachment."}}}});
                    Workspace.Map.Rewards.Add(new MapRewardSpec{Id="probe-pending",Name="pending probe",Contents=new List<DeviceLoot>{new DeviceLoot{Def=new DefRef{Name="MedicineIndustrial"},Count=2}}});
                    Stage=11;Frames=0;return;
                }
                if(Stage==11){
                    // Let the editor establish its viewport before recording the camera to preserve.
                    if(++Frames<20)return;
                    var t=Traverse.Create(Workspace);var view=t.Field("mapView").GetValue<MapViewport>();
                    centerX=view.CenterX;centerZ=view.CenterY;
                    Stage=2;LongEventHandler.ExecuteWhenFinished(()=>t.Method("HotTest").GetValue());return;
                }
                if(Stage==1){
                    Frames++;
                    var t=Traverse.Create(Workspace);var view=t.Field("mapView").GetValue<MapViewport>();
                    if(Frames==31){
                        EditorInteractionAudit.PointerSelection();
                        EditorInteractionAudit.Readability();
                        EditorInteractionAudit.Authoring(Workspace);
                        var route=new PatrolRoute{Id="patrol-editor-probe",Name="unbound route probe"};Workspace.Map.PatrolRoutes.Add(route);t.Field("patrolRoute").SetValue(route.Id);
                        t.Method("AddPatrolPoint",new Cell{X=5,Z=5}).GetValue();t.Method("AddPatrolPoint",new Cell{X=7,Z=5}).GetValue();
                        Check(route.Points.Count==2&&!Workspace.Map.CombatPatrols.Any(b=>b.Route==route.Id),"native editor draws independent route before host binding");
                        t.Field("selection").SetValue(new HashSet<string>(route.Points.Select(p=>p.Marker)));t.Field("selectedId").SetValue(route.Points[0].Marker);
                        t.Method("BeginSelectionMove",new Cell{X=5,Z=5}).GetValue();t.Method("PreviewSelectionMove",new Cell{X=8,Z=6}).GetValue();t.Method("FinishSelectionMove").GetValue();
                        Check(Workspace.Map.PatrolRoutes.Last().Points.Select(p=>p.Position.X).SequenceEqual(new[]{8,10}),"native batch drag updates waypoint positions");
                        var history=t.Field("history").GetValue<History<Project>>();history.Undo();Check(Workspace.Map.PatrolRoutes.Last().Points[0].Position.X==5,"native waypoint drag undo");history.Redo();
                        var oldBindings=Json.Copy(Workspace.Map.CombatPatrols);var host=oldBindings[0].Pawn;t.Field("selection").SetValue(new HashSet<string>{host});t.Field("selectedId").SetValue(host);
                        t.Method("BindPatrol",route.Id).GetValue();Check(Workspace.Map.CombatPatrols.Single(b=>b.Pawn==host).Route==route.Id,"native route can bind host after drawing");Workspace.Map.CombatPatrols=oldBindings;
                        t.Field("selection").SetValue(new HashSet<string>());t.Field("selectedId").SetValue("");view.Fit();centerX=view.CenterX;centerZ=view.CenterY;return;
                    }
                    if(Frames==60){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"mission-two-editor.png"));return;}
                    if(Frames==65){var rule=Workspace.Map.Rules.Single(r=>r.Id==EditorInteractionAudit.TriggerId);t.Method("FocusObject",rule).GetValue();return;}
                    if(Frames==72){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"spatial-trigger-editor.png"));return;}
                    if(Frames==76){t.Method("SwitchWorkspace","Logic").GetValue();return;}
                    if(Frames==84){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"story-workspace.png"));return;}
                    if(Frames==88){t.Method("SwitchWorkspace","Settings").GetValue();t.Field("settingsTab").SetValue("Modules");return;}
                    if(Frames==96){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"map-component-settings.png"));return;}
                    if(Frames==100){t.Method("SwitchWorkspace","Map").GetValue();view.Fit();centerX=view.CenterX;centerZ=view.CenterY;return;}
                    if(Frames==105){t.Method("FocusObject",Workspace.Map.Objects.First(o=>o.Layer=="Patrol"&&Workspace.Map.PatrolRoutes.Any(route=>route.Points.Any(p=>p.Marker==o.Id)&&Workspace.Map.CombatPatrols.Any(b=>b.Route==route.Id)))).GetValue();return;}
                    if(Frames==112){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"waypoint-bindings.png"));return;}
                    if(Frames==115){t.Field("inspectorTab").SetValue("Properties");return;}
                    if(Frames==122){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"waypoint-properties.png"));return;}
                    if(Frames==125){t.Field("inspectorTab").SetValue("Overview");var s=Workspace.Map.Interactions.First(i=>i.BlockCells.Count>0);t.Method("FocusObject",Workspace.Map.Objects.Single(o=>o.Id==s.Host)).GetValue();return;}
                    if(Frames==132){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"interactive-wall.png"));return;}
                    if(Frames==135){t.Method("FocusObject",Workspace.Map.Objects.Single(o=>o.Id=="m2-energy")).GetValue();return;}
                    if(Frames==142){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"energy-purpose.png"));return;}
                    if(Frames==145){view.Fit();centerX=view.CenterX;centerZ=view.CenterY;return;}
                    if(Frames>160){Stage=2;Frames=0;LongEventHandler.ExecuteWhenFinished(()=>t.Method("HotTest").GetValue());}return;
                }
                if(Stage==4&&++Frames>30){
                    Check(Find.WindowStack.Windows.Contains(Workspace),"main-menu return restores the same editor window");
                    Check(Workspace.Map.Id=="romulus-outpost"&&Workspace.Map.Objects.Any(o=>o.Id=="unsaved-hot-probe"),"editor map selection and authored state preserved");
                    var view=Traverse.Create(Workspace).Field("mapView").GetValue<MapViewport>();
                    Check(Math.Abs(view.CenterX-centerX)<1&&Math.Abs(view.CenterY-centerZ)<1,"editor camera position restored");
                    Check(!Workspace.Map.Components.Any(s=>s.Type==typeof(StealthMapComponent).FullName),"live hot-plug edits do not mutate authoring configuration");
                    ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"returned-editor.png"));Stage=5;Frames=0;return;
                }
                if(Stage==5&&++Frames>30){
                    var errors=Log.Messages.Where(x=>x.type==LogMessageType.Error).Select(x=>x.text).ToList();File.WriteAllLines(Path.Combine(Folder,"errors.txt"),errors);Check(errors.Count==0,"zero native errors during hot-test and return");
                    File.WriteAllText(Path.Combine(Folder,"complete.txt"),"PASS");Stage=99;Application.Quit();
                }
            }catch(Exception ex){Fail(ex);}
        }
    }
    [HarmonyPatch(typeof(Root),nameof(Root.Update))]
    static class HotTestAuditFrame { static void Postfix(){if(HotTestAudit.Stage>0)HotTestAudit.Tick();} }
    // Drive a hover through the real UI event for the isolated screenshot audit.
    [HarmonyPatch(typeof(Workspace),nameof(Workspace.DoWindowContents))]
    static class HotTestAuditHover {
        static void Prefix(Rect r,out Vector2 __state){
            __state=Event.current.mousePosition;
            if(HotTestAudit.Enabled&&HotTestAudit.Stage==1&&HotTestAudit.Frames>=68&&HotTestAudit.Frames<=74&&Event.current.type==EventType.Repaint)
                Event.current.mousePosition=new Vector2(r.width-205,52);
        }
        static void Postfix(Vector2 __state){Event.current.mousePosition=__state;}
    }
    public sealed class HotTestAuditGame : GameComponent {
        int stage,frames;bool loading;
        public HotTestAuditGame(Game game){}
        public override void ExposeData(){Scribe_Values.Look(ref stage,"rcHotTestAuditStage");}
        public override void GameComponentUpdate(){
            if(!HotTestAudit.Enabled||loading||HotTestAudit.Stage==99||++frames<90)return;
            var c=Current.Game.GetComponent<CampaignController>();
            if(c?.Active!=true||c.Map==null||c.Transitioning||c.EntryPending||c.Presentation||HotTestSession.SavePending)return;
            try{
                if(stage==0){
                    var map=c.Map;var actor=c.Actors["hero"];
                    if(!GenCommandLine.CommandLineArgPassed("rc-loading-audit"))EditorInteractionAudit.Runtime(c);
                    HotTestAudit.Check(c.NodeId=="mission-two"&&c.CampaignId=="pmp_archive","hot test starts selected mission in original campaign");
                    HotTestAudit.Check(c.MapSpec.Objects.Any(o=>o.Id=="unsaved-hot-probe"),"hot test uses current unsaved editor state");
                    HotTestAudit.Check(File.Exists(GenFilePaths.FilePathForSavedGame(HotTestSession.AutoSaveName)),"immediate native hot-test autosave exists");
                    HotTestAudit.Check(map.Size.x==280&&map.Size.z==280,"mission two has native 280 x 280 map");
                    HotTestAudit.Check(map.GetComponent<StealthMapComponent>()==null,"native component discovery does not auto-attach stealth");
                    var authoredGuards=c.MapSpec.Objects.Where(p=>p.Def?.Name=="PMP_OutpostGuard"&&p.SpawnAtStart).ToList();
                    HotTestAudit.Check(authoredGuards.All(p=>c.Objects.TryGetValue(p.Id,out var thing)&&thing is Pawn guard&&(guard.Spawned||guard.Dead)),"all authored guards present or killed in the opening battle");
                    HotTestAudit.Check(map.listerThings.AllThings.Count(t=>t.def.defName=="PMP_RomulusSpearTurret")==c.MapSpec.Objects.Count(p=>p.Def?.Name=="PMP_RomulusSpearTurret"&&p.SpawnAtStart),"all authored Romulus turrets spawned");
                    HotTestAudit.Check(!map.listerThings.AllThings.Any(t=>t.def.defName=="Table2x2c"||t.def.defName=="Stool"),"outpost contains no tables or stools");
                    HotTestAudit.Check(actor.equipment.Primary?.def.defName=="PM_GunC7Templar"&&actor.health.hediffSet.hediffs.Any(h=>h.def.defName=="PMPH_HediffProtossHeart"),"direct hot test initializes C7 and preserves protoss heart");
                    var module=map.GetComponent<CombatPatrolMapComponent>();
                    if(!GenCommandLine.CommandLineArgPassed("rc-loading-audit")){
                    foreach(var id in new[]{"m2-forge","S0","S1","side-A","centurion","knight-scene","escape-north","escape-west","extraction"}){
                        var o=c.MapSpec.Objects.Single(p=>p.Id==id);HotTestAudit.Check(actor.CanReach(new IntVec3(o.Position.X,0,o.Position.Z),PathEndMode.Touch,Danger.Deadly),"native path to "+id);
                    }
                    foreach(var binding in c.MapSpec.CombatPatrols){
                        var pawn=(Pawn)c.Objects[binding.Pawn];var route=c.MapSpec.PatrolRoutes.Single(r=>r.Id==binding.Route);
                        if(pawn.Dead||pawn.Downed||!pawn.Spawned){HotTestAudit.Check(true,"patrol actor unavailable after opening combat: "+binding.Pawn+" dead="+pawn.Dead+" downed="+pawn.Downed);continue;}
                        HotTestAudit.Check(route.Points.All(p=>pawn.CanReach(new IntVec3(p.Position.X,0,p.Position.Z),PathEndMode.OnCell,Danger.Deadly)),"native patrol route reachable: "+binding.Pawn+" at="+pawn.Position);
                    }
                    foreach(var route in c.MapSpec.PatrolRoutes.Where(r=>c.MapSpec.CombatPatrols.Any(b=>b.Route==r.Id)))HotTestAudit.Check(route.Points.All(p=>actor.CanReach(new IntVec3(p.Position.X,0,p.Position.Z),PathEndMode.OnCell,Danger.Deadly)),"all assigned route points remain reachable from the player: "+route.Id);
                    var temporaryRubble=new List<Thing>();
                    try{
                        var rubble=ThingDefOf.CollapsedRocks;
                        HotTestAudit.Check(c.MapSpec.Interactions.Where(s=>s.BlockCells.Count>0).All(s=>s.ObstacleDef==rubble.defName&&!s.DestroyHost&&c.Objects[s.Host].def.defName=="PMP_RomulusRedWall"),"all collapses use native rocks and retain flanking wall activation hosts");
                        foreach(var at in c.MapSpec.Interactions.SelectMany(s=>s.BlockCells).Select(p=>new IntVec3(p.X,0,p.Z)).Distinct())
                            if(at.GetEdifice(map)==null)temporaryRubble.Add(GenSpawn.Spawn(ThingMaker.MakeThing(rubble),at,map));
                        foreach(var id in new[]{"m2-forge","S0","side-A","centurion","knight-scene","escape-north","escape-west","extraction"}){
                            var o=c.MapSpec.Objects.Single(p=>p.Id==id);
                            HotTestAudit.Check(actor.CanReach(new IntVec3(o.Position.X,0,o.Position.Z),PathEndMode.Touch,Danger.Deadly),"all 16 collapsed barriers retain native path to "+id);
                        }
                    }finally{foreach(var rubble in temporaryRubble)if(!rubble.Destroyed)rubble.Destroy(DestroyMode.Vanish);}
                    var positions=map.mapPawns.AllPawnsSpawned.ToDictionary(p=>p.ThingID,p=>p.Position);
                    for(int tick=0;tick<360;tick++)Find.TickManager.DoSingleTick();
                    HotTestAudit.Check(module.Units.Any(u=>map.mapPawns.AllPawnsSpawned.Any(p=>p.ThingID==u.PawnId&&p.Position!=positions[p.ThingID])),"ordinary guards move on their routes without stealth");
                    }
                    var book=(Book)c.RewardStocks.Single(s=>s.Id=="probe-reward").GetDirectlyHeldThings().Single();HotTestAudit.RewardId=book.ThingID;
                    HotTestAudit.PendingId=c.RewardStocks.Single(s=>s.Id=="probe-pending").GetDirectlyHeldThings().Single().ThingID;
                    HotTestAudit.Check(book.Title=="Reward identity probe"&&book.IsReadable,"reward book generated and initialized before interaction");
                    bool fire=actor.drafter.FireAtWill,drafted=actor.Drafted;
                    var stealthSpec=new MapComponentSpec{Type=typeof(StealthMapComponent).FullName,Enabled=false};
                    CampaignMapModules.Add(c,stealthSpec);
                    HotTestAudit.Check(map.GetComponent<StealthMapComponent>()!=null&&StealthMapComponent.For(map)==null&&actor.drafter.FireAtWill==fire,"disabled module attachment has no control side effects");
                    CampaignMapModules.Enable(c,stealthSpec.Type,true);HotTestAudit.Check(StealthMapComponent.For(map)!=null&&!actor.drafter.FireAtWill,"live enabling activates stealth controls");
                    CampaignMapModules.Remove(c,stealthSpec.Type);HotTestAudit.Check(map.GetComponent<StealthMapComponent>()==null&&actor.drafter.FireAtWill==fire&&actor.Drafted==drafted,"removing stealth restores control and removes instance");
                    HotTestAudit.Check(c.RewardStocks.Single(s=>s.Id=="probe-reward").GetDirectlyHeldThings().Single().ThingID==HotTestAudit.RewardId,"hot plugging does not regenerate rewards");
                    HotTestAudit.Check(c.CollectMapReward("probe-reward")&&c.CollectMapReward("probe-reward")&&actor.inventory.innerContainer.Count(t=>t.ThingID==HotTestAudit.RewardId)==1,"collection works without stealth and transfers the original item once");
                    CampaignMapModules.Enable(c,typeof(CombatPatrolMapComponent).FullName,false);
                    HotTestAudit.Check(!module.Enabled,"live patrol disable releases owned movement");CampaignMapModules.Enable(c,typeof(CombatPatrolMapComponent).FullName,true);
                    Find.CameraDriver.SetRootPosAndSize(new Vector3(140,0,125),55);
                    ScreenCapture.CaptureScreenshot(Path.Combine(HotTestAudit.Folder,"mission-two-game.png"));
                    stage=1;loading=true;GameDataSaveLoader.SaveGame("HotTest-Module-Roundtrip");
                    LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("HotTest-Module-Roundtrip"));return;
                }
                if(stage==1){
                    HotTestAudit.Check(c.Map.GetComponent<CombatPatrolMapComponent>()?.Enabled==true&&c.Map.GetComponent<StealthMapComponent>()==null,"module attachment state survives same-version save/load");
                    HotTestAudit.Check(c.Actors["hero"].inventory.innerContainer.Any(t=>t.ThingID==HotTestAudit.RewardId)&&c.RewardStocks.Single(s=>s.Id=="probe-reward").Collected,"collected reward identity and state survive save/load");
                    HotTestAudit.Check(c.RewardStocks.Single(s=>s.Id=="probe-pending").GetDirectlyHeldThings().Single().ThingID==HotTestAudit.PendingId,"uncollected generated rewards survive save/load");
                    HotTestAudit.Check(c.CollectMapReward("probe-pending"),"uncollected stock can be claimed after loading");
                    HotTestAudit.Stage=4;HotTestAudit.Frames=0;stage=2;GenScene.GoToMainMenu();
                }
            }catch(Exception ex){HotTestAudit.Fail(ex);}
        }
    }
}
