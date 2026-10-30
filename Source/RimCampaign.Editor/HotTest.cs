using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public static class HotTestSession
    {
        public static Workspace Editor;
        public static Game Owner;
        public static bool Active,ReturnPending,SavePending;
        public static string AutoSaveName,ProjectSnapshot;
        public static void Start(Workspace workspace,string projectPath,Project project,string mapId){
            if(Current.ProgramState==ProgramState.Playing)throw new InvalidOperationException(RimCampaign.Runtime.RcText.T("请先返回主菜单，再从创作界面开始热测试。"));
            var matches=project.Campaigns.SelectMany(c=>c.Nodes.Where(n=>n.Map==mapId).Select(n=>new{Campaign=c,Node=n})).ToList();
            if(matches.Count!=1)throw new InvalidOperationException(matches.Count==0?RimCampaign.Runtime.RcText.T("当前地图尚未连接到战役关卡。"):RimCampaign.Runtime.RcText.T("同一地图对应多个战役节点，请先明确唯一测试节点。"));
            var campaign=matches[0].Campaign;var node=matches[0].Node;
            var folder=Path.Combine(Workspace.WorkRoot,"HotTests");Directory.CreateDirectory(folder);
            ProjectSnapshot=Path.Combine(folder,"latest.project.json.gz");
            Editor=workspace;Active=SavePending=true;ReturnPending=false;
            AutoSaveName="RimCampaign-HotTest-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            workspace.Close(false);
            LongEventHandler.QueueLongEvent(()=>{
                var watch=System.Diagnostics.Stopwatch.StartNew();
                LongEventHandler.SetCurrentEventText(RcText.T("校验战役工程…"));
                var issues=Validation.Check(project);if(issues.Count>0)throw new InvalidOperationException(string.Join("\n",issues));
                LongEventHandler.SetCurrentEventText(RcText.T("保存热测试快照…"));
                ProjectFiles.Save(projectPath,project);
                // Serialize once; the snapshot and runtime copy both come from those same saved bytes.
                Json.CopyFile(projectPath,ProjectSnapshot);
                LongEventHandler.SetCurrentEventText(RcText.T("加载热测试快照…"));
                var snapshot=Json.Load<Project>(ProjectSnapshot);
                Log.Message("[RimCampaign hot test] Validate/save/load snapshot: "+watch.ElapsedMilliseconds+" ms");
                LongEventHandler.SetCurrentEventText(RcText.T("生成战役地图…"));
                Root_Play.SetupForQuickTestPlay();
                Owner=Current.Game;
                if(node.Kind=="Open"){
                    var scenario=DefDatabase<ScenarioDef>.AllDefsListForReading.FirstOrDefault(d=>d.scenario.AllParts.OfType<ScenPart_Campaign>().Any(p=>p.campaign?.campaignId==campaign.Id));
                    if(scenario!=null){Current.Game.Scenario=scenario.scenario;Find.Scenario.PreConfigure();}
                }
                var controller=Current.Game.GetComponent<CampaignController>();controller.Prepare(snapshot,campaign.Id,node.Id);
                // Quick-test world generation already created the player faction.
                // Only campaign setup is new; repeating every scenario part would
                // create a second player faction and corrupt goodwill/ideology state.
                if(node.Kind=="Open")foreach(var part in Find.Scenario.AllParts.OfType<ScenPart_Campaign>())part.PostWorldGenerate();
                // Vanilla starting-pawn preparation assigns skills/work priorities.
                // Mechanical actors remain owned by the campaign arrival pipeline.
                var starters=controller.Actors.Values.Distinct().Where(p=>p.skills!=null&&p.workSettings!=null).ToList();
                Find.GameInitData.startingAndOptionalPawns=starters;
                Find.GameInitData.startingPawnCount=starters.Count;
                Find.GameInitData.mapSize=node.Kind=="Open"?250:controller.MapSpec.Width;
                Find.GameInitData.mapGeneratorDef=node.Kind=="Open"?null:DefDatabase<MapGeneratorDef>.GetNamed("RC_Pocket");
                LongEventHandler.ExecuteWhenFinished(()=>{
                    if(node.Kind=="Open")Find.WindowStack.Add(new HotTestLandingPage{nextAct=QueuePlay});
                    else QueuePlay();
                });
            },"GeneratingMap",true,Failed);
        }
        static void QueuePlay(){
            // Do not attach a scene change to fallible preparation: the native
            // long-event handler can still load that scene after its action fails.
            LongEventHandler.QueueLongEvent(()=>{
                Find.GameInitData.PrepForMapGen();Find.Scenario.PreMapGenerate();
                LongEventHandler.ExecuteWhenFinished(()=>LongEventHandler.QueueLongEvent(()=>{},"Play","GeneratingMap",false,Failed));
            },"GeneratingMap",true,Failed);
        }
        static void Failed(Exception ex){
            ReturnPending=true;Log.Error("[RimCampaign hot test] "+ex);GenScene.GoToMainMenu();
        }
        sealed class HotTestLandingPage : Page_SelectStartingSite {
            protected override void DoBack(){base.DoBack();ReturnPending=true;GenScene.GoToMainMenu();}
        }
        public static void ReturnToEditor(){
            if(!ReturnPending||Current.ProgramState!=ProgramState.Entry||Editor==null)return;
            ReturnPending=false;Active=false;SavePending=false;Owner=null;
            var editor=Editor;Editor=null;
            LongEventHandler.ExecuteWhenFinished(()=>Find.WindowStack.Add(editor));
        }
    }
    public partial class Workspace
    {
        void HotTest(){
            try{
                CancelPointerGestures();
                if(string.IsNullOrEmpty(path))path=Path.Combine(WorkRoot,"Projects",Project.Id+".json");
                HotTestSession.Start(this,path,Project,Map.Id);
            }catch(Exception ex){status=ex.Message;EditorUI.Error(ex);}
        }
        void MapComponents()=>Find.WindowStack.Add(new MapComponentsWindow(Project,Map,draft=>Edit(_=>Map.Components=draft.Components)));
    }
    [HarmonyPatch(typeof(GenScene),nameof(GenScene.GoToMainMenu))]
    static class HotTestReturn {
        static void Prefix(){if(HotTestSession.Active&&HotTestSession.Editor!=null)HotTestSession.ReturnPending=true;}
    }
    public sealed class HotTestAutoSave : GameComponent
    {
        int frames;bool queued;
        public HotTestAutoSave(Game game){}
        public override void GameComponentUpdate(){
            if(queued||!HotTestSession.Active||!HotTestSession.SavePending||HotTestSession.Owner!=Current.Game)return;
            var c=Current.Game.GetComponent<CampaignController>();
            if(c?.Active!=true||c.Map==null||c.EntryPending||c.Transitioning||c.Presentation||++frames<90)return;
            queued=true;
            LongEventHandler.QueueLongEvent(()=>{
                if(HotTestSession.Owner!=Current.Game)return;
                // A cinematic may start between scheduling this event and executing it.
                // The campaign deliberately defers saves during presentation; retry afterwards.
                if(c.EntryPending||c.Transitioning||c.Presentation){queued=false;frames=0;return;}
                GameDataSaveLoader.SaveGame(HotTestSession.AutoSaveName);
                if(!File.Exists(GenFilePaths.FilePathForSavedGame(HotTestSession.AutoSaveName)))throw new IOException("Hot-test save was not created.");
                HotTestSession.SavePending=false;
                LongEventHandler.ExecuteWhenFinished(()=>Messages.Message(RimCampaign.Runtime.RcText.T("热测试已自动保存。退出到主菜单后返回原地图编辑器。"),MessageTypeDefOf.SilentInput,false));
            },"SavingLongEvent",false,ex=>{Log.Error("[RimCampaign hot-test save] "+ex);});
        }
        public override void GameComponentOnGUI(){
            if(!HotTestSession.Active||HotTestSession.Owner!=Current.Game||Current.Game?.GetComponent<CampaignController>()?.Active!=true)return;
            if(EditorUI.Button(new Rect(UI.screenWidth-210,100,195,30),RimCampaign.Runtime.RcText.T("热测试：地图组件…"))){
                var c=Current.Game.GetComponent<CampaignController>();
                Find.WindowStack.Add(new MapComponentsWindow(c.Project,c.MapSpec,null,c));
            }
        }
    }
}
