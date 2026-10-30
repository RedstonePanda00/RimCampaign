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
using Region = RimCampaign.Core.Region;

namespace RimCampaign.Editor
{
    [StaticConstructorOnStartup]
    public static class EditorBoot
    {
        static EditorBoot(){new Harmony("rimcampaign.editor").PatchAll();}
    }
    [HarmonyPatch(typeof(MainMenuDrawer),nameof(MainMenuDrawer.DoMainMenuControls))]
    static class MenuEntry
    {
        static void Postfix(Rect rect) {
            HotTestSession.ReturnToEditor();
            if(Current.ProgramState==ProgramState.Entry&&EditorUI.Button(new Rect(rect.x-220,rect.y,205,45),RimCampaign.Runtime.RcText.T("RimCampaign 创作")))Find.WindowStack.Add(new Workspace());
        }
    }
    public partial class Workspace : Window
    {
        History<Project> history;string mapId,tab="Map",search="",source="",category="Thing",status="",path="";
        Vector2 assetsScroll,sceneScroll,graphOffset=new Vector2(20,20);float left=235,right=270;
        readonly MapViewport mapView=new MapViewport();
        Texture2D terrainPreview;MapSpec terrainPreviewMap;bool terrainPreviewDirty=true;
        Vector2 canvasOffset { get=>new Vector2(mapView.X,mapView.Y); set=>mapView.SetOffset(value.x,value.y); }
        float zoom=>mapView.Zoom;
        bool leftOpen=true,rightOpen=true,dragLeft,dragRight,showAux=true,lockAux;
        Placement brush;string tool="",selectedId="";HashSet<string> selection=new HashSet<string>();
        List<Placement> clipboard=new List<Placement>();Vector2 dragStart;Cell origin;bool dragging;
        bool painting;string paintSnapshot;Placement paintBrush;Cell paintLast;readonly HashSet<int> painted=new HashSet<int>();
        bool panning,panMoved;int panButton;Vector2 panStart,panLast;
        int focusRevision;
        bool applicationWasFocused=Application.isFocused;
        bool focusCancellationPending;
        Dictionary<string,Cell> originalPositions=new Dictionary<string,Cell>();
        public static string WorkRoot=>Path.Combine(GenFilePaths.SaveDataFolderPath,"RimCampaign");
        public Project Project=>history.Value;
        public MapSpec Map { get { var maps=Project.Maps;for(int i=0;i<maps.Count;i++)if(maps[i].Id==mapId)return maps[i];return maps[0]; } }
        public override Vector2 InitialSize=>new Vector2(UI.screenWidth-24,UI.screenHeight-24);
        public Workspace() {
            forcePause=true;doCloseX=true;absorbInputAroundWindow=true;preventCameraMotion=true;
            var p=NewProject();history=new History<Project>(p);mapId=p.Maps[0].Id;
        }
        static Project NewProject(){
            var m=new MapSpec{Name=RimCampaign.Runtime.RcText.T("地图 1")};m.Hud.Add(new HudWidget{Kind="Objectives",Name=RimCampaign.Runtime.RcText.T("目标列表"),Height=120});
            return new Project{Name=RimCampaign.Runtime.RcText.T("新战役"),Maps=new List<MapSpec>{m}};
        }
        void Edit(Action<Project> change){try{history.Edit(p=>{change(p);if(p.Maps.Count>0){PatrolAuthoring.Synchronize(Map);TerrainInitializer.ClearLayoutOverlaps(Map);}});}finally{InvalidateScene();}}
        void ObserveApplicationFocus(){
            if(applicationWasFocused&&!Application.isFocused)focusCancellationPending=true;
            applicationWasFocused=Application.isFocused;
        }
        public override void WindowUpdate(){base.WindowUpdate();ObserveApplicationFocus();}
        public override void DoWindowContents(Rect r) {
            try{
                ObserveApplicationFocus();
                if(focusCancellationPending){CancelPointerGestures();focusCancellationPending=false;}
                if(focusRevision!=EditorFocus.Revision){EditorFocus.Clear();focusRevision=EditorFocus.Revision;}
                EnsureSceneIndex();
                DrawEditorShell(r);
                if(Application.isFocused)HandleKeys();
            }catch(Exception ex){status=ex.GetBaseException().Message;Log.ErrorOnce("[RimCampaign editor] "+ex,ex.GetType().GetHashCode());}
        }
        internal string TryResizeMap(int width,int height) {
            var error=MapSizeError(Map,width,height);if(error!=null)return error;
            Edit(_=>{Map.Width=width;Map.Height=height;if(Map.TerrainInitialization!=null)TerrainInitializer.RegenerateUnderLayout(Map);});
            status=RimCampaign.Runtime.RcText.T("地图尺寸已改为 ")+width+" × "+height+RimCampaign.Runtime.RcText.T("；可撤销。");
            return null;
        }
        internal static string MapSizeError(MapSpec map,int width,int height) {
            if(width<20||height<20||width>500||height>500)return RimCampaign.Runtime.RcText.T("宽和高都必须是 20–500 之间的整数。");
            bool Outside(Cell c)=>c==null||c.X<0||c.Z<0||c.X>=width||c.Z>=height;
            var blocked=new List<string>();
            foreach(var p in map.Objects) {
                if(TerrainInitializer.Generated(p))continue;
                bool outside=Outside(p.Position);
                var def=p.Kind=="Thing"?DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name):null;
                if(!outside&&def!=null) {
                    var bounds=GenAdj.OccupiedRect(new IntVec3(p.Position.X,0,p.Position.Z),new Rot4(p.Rotation),def.size);
                    outside=bounds.minX<0||bounds.minZ<0||bounds.maxX>=width||bounds.maxZ>=height;
                }
                if(outside)blocked.Add(p.Name);
            }
            blocked.AddRange(map.Regions.Concat(map.NaturalClearings).Where(a=>Outside(a.Min)||Outside(a.Max)).Select(a=>a.Name));
            blocked.AddRange(map.Rules.Where(a=>TriggerGeometry.Visible(a)&&Outside(a.Position)).Select(a=>a.Name));
            blocked.AddRange(map.Stealth.Routes.Concat(map.PatrolRoutes).Where(a=>a.Points.Any(p=>Outside(p.Position))).Select(a=>a.Name));
            blocked.AddRange(map.Stealth.Devices.Where(a=>string.IsNullOrEmpty(a.Host)&&Outside(a.Position)).Select(a=>a.Name));
            return blocked.Count==0?null:RimCampaign.Runtime.RcText.T("有 ")+blocked.Count+RimCampaign.Runtime.RcText.T(" 个对象、区域或触发器会超出边界。请先移动或删除：\n")+string.Join("、",blocked.Take(6));
        }
        void OpenProject(string input) {
            var resolved=ProjectFiles.ResolvePath(input,true);
            CancelPointerGestures();
            LongEventHandler.QueueLongEvent(()=>{
                var watch=System.Diagnostics.Stopwatch.StartNew();
                bool missionFile=resolved.EndsWith(".rcmission.json",StringComparison.OrdinalIgnoreCase);
                Project loaded;
                if(missionFile){var mission=Json.Load<MissionMapSpec>(resolved);MissionMaps.Validate(mission);loaded=mission.Project;}
                else loaded=ProjectFiles.Load(resolved);
                LongEventHandler.ExecuteWhenFinished(()=>{
                    history=new History<Project>(loaded);mapId=loaded.Maps[0].Id;path=missionFile?"":resolved;
                    mapView.Reset();selectedId="";selection.Clear();inlineEditor=null;InvalidateScene();
                    status=RimCampaign.Runtime.RcText.T("已加载 ")+resolved;
                });
                Log.Message("[RimCampaign editor] Project load: "+watch.ElapsedMilliseconds+" ms");
            },"LoadingLongEvent",true,ex=>{status=ex.Message;LongEventHandler.ExecuteWhenFinished(()=>EditorUI.Error(ex));});
        }
        void Save() {
            if(path==""){Prompt(RimCampaign.Runtime.RcText.T("保存项目 JSON"),Path.Combine(WorkRoot,"Projects",Project.PackageId+".project.json.gz"),s=>{ProjectFiles.Save(s,Project);path=ProjectFiles.ResolvePath(s,false);status=RimCampaign.Runtime.RcText.T("已保存 ")+path;});return;}
            try{ProjectFiles.Save(path,Project);status=RimCampaign.Runtime.RcText.T("已保存 ")+path;}catch(Exception ex){EditorUI.Error(ex);}
        }
        public static void Prompt(string title,string value,Action<string> done)=>Find.WindowStack.Add(new TextPrompt(title,value,done));
        void Export() {
            var report=Dependencies.Scan(Project,CampaignDef.Catalog());var issues=Validation.Check(Project);issues.AddRange(PlacementIssues());
            if(issues.Count>0||report.Errors.Count>0){status=RimCampaign.Runtime.RcText.T("导出校验失败");Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n",issues.Select(i=>i.ToString()).Concat(report.Errors))));return;}
            Prompt(RimCampaign.Runtime.RcText.T("导出父目录"),Path.Combine(WorkRoot,"Exports"),s=>{
                var result=Exporter.Export(Project,s,report,path==""?null:Path.GetDirectoryName(path));status=RimCampaign.Runtime.RcText.T("已导出 ")+result;
                Find.WindowStack.Add(new Dialog_MessageBox(RimCampaign.Runtime.RcText.T("战役包已生成：\n")+result+RimCampaign.Runtime.RcText.T("\n\n启用该子模组并重新加载游戏后，在新游戏剧本中选择战役。")));
            });
        }
        void Validate(){RefreshProblems();SwitchWorkspace("Problems");}
        IEnumerable<Issue> PlacementIssues(){
            foreach(var placement in Project.Maps.SelectMany(m=>m.Objects).Concat(Project.Campaigns.SelectMany(c=>c.Nodes).SelectMany(n=>n.Resources))){
                string error=null;try{PlacementStorage.Validate(placement);}catch(Exception ex){error=ex.Message;}
                if(error!=null)yield return new Issue{Path=placement.Id,Message=error};
            }
        }
        void Compile(string sourcePath) {
            var output=ExtensionCompiler.Compile(Project,sourcePath);Edit(p=>{p.CustomAssemblies.RemoveAll(a=>Path.GetFileName(a)==Path.GetFileName(output));p.CustomAssemblies.Add(output);});status=RimCampaign.Runtime.RcText.T("编译成功；导出并重新加载模组后生效。");
        }
        string assetListKey;List<Def> assetList;
        void AssetLibrary(Rect r) {
            GUI.BeginGroup(r);
            search=Widgets.TextField(new Rect(0,0,r.width,29),search);
            if(EditorUI.Button(new Rect(0,34,r.width,27),source==""?RimCampaign.Runtime.RcText.T("全部模组"):source))EditorUI.Pick(new[]{""}.Concat(LoadedModManager.RunningMods.Select(m=>m.PackageIdPlayerFacing)),s=>source=s);
            if(EditorUI.Button(new Rect(0,65,r.width,27),AssetCategoryLabel(category)))EditorUI.Pick(new[]{"Thing","Floor","Terrain","Roof","PawnKind","Pawn","Boss","Marker","Region","Trigger"}.Select(v=>new KeyValuePair<string,string>(v,AssetCategoryLabel(v))),s=>{
                if(s=="Floor"||s=="Terrain")SetEditMode(s);
                else{if(editMode!="Edit")SetEditMode("Edit");category=s;}
            });
            if(category=="Boss") {
                if(Project.Bosses==null)Project.Bosses=new List<BossTemplate>();
                if(EditorUI.Button(new Rect(0,100,r.width,32),RimCampaign.Runtime.RcText.T("+ Boss 模板")))Edit(p=>p.Bosses.Add(new BossTemplate{Name=RimCampaign.Runtime.RcText.T("新 Boss")}));
                Widgets.BeginScrollView(new Rect(0,140,r.width,r.height-140),ref assetsScroll,new Rect(0,0,r.width-20,Project.Bosses.Count*72));
                for(int i=0;i<Project.Bosses.Count;i++) {
                    var boss=Project.Bosses[i];float y=i*72;
                    if(EditorUI.Button(new Rect(0,y,r.width-24,32),RimCampaign.Runtime.RcText.T("放置 · ")+boss.Name)){EditorFocus.Release();brush=new Placement{Kind="Boss",Boss=boss.Id,Name=boss.Name,Faction="Hostile"};tool="Place";}
                    if(EditorUI.Button(new Rect(0,y+34,r.width-24,30),RimCampaign.Runtime.RcText.T("编辑阶段与行为")))Find.WindowStack.Add(new ObjectWindow(boss,o=>Edit(p=>p.Bosses[p.Bosses.FindIndex(b=>b.Id==boss.Id)]=(BossTemplate)o),Project));
                }
                Widgets.EndScrollView();GUI.EndGroup();return;
            }
            if(new[]{"Pawn","Marker","Region","Trigger"}.Contains(category)) {
                if(EditorUI.Button(new Rect(0,105,r.width,35),RimCampaign.Runtime.RcText.T("放置 ")+AssetCategoryLabel(category))){EditorFocus.Release();tool=category;brush=null;}
                GUI.EndGroup();return;
            }
            var type=category=="PawnKind"?"PawnKindDef":category=="Terrain"||category=="Floor"?"TerrainDef":category=="Roof"?"RoofDef":"ThingDef";
            var key=category+"\n"+source+"\n"+search+"\n"+LanguageDatabase.activeLanguage.folderName;
            if(assetList==null||assetListKey!=key){
            assetListKey=key;
            assetList=EditorUI.Defs(type).Where(d=>(source==""||d.modContentPack?.PackageIdPlayerFacing==source)&&
                ((d.label??"").IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0||d.defName.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)).
                Where(d=>!(d is ThingDef t)||t.category!=ThingCategory.Pawn).
                Where(d=>!(d is TerrainDef terrain)||TerrainSurfaces.IsFloor(terrain)==(category=="Floor")).ToList();
            }
            var list=assetList;
            float listTop=98;
            if(category=="Terrain"||category=="Floor"||category=="Roof"){
                if(EditorUI.Button(new Rect(0,98,r.width,32),category=="Roof"?RimCampaign.Runtime.RcText.T("擦除屋顶（含厚岩顶）"):category=="Floor"?RimCampaign.Runtime.RcText.T("擦除地板 → 露出底层地形"):RimCampaign.Runtime.RcText.T("恢复默认地形（保留地板）"))){
                    EditorFocus.Release();
                    brush=new Placement{Kind=category=="Roof"?"EraseRoof":category=="Floor"?"EraseFloor":"EraseTerrain",Name=category=="Roof"?RimCampaign.Runtime.RcText.T("擦除屋顶"):category=="Floor"?RimCampaign.Runtime.RcText.T("擦除地板"):RimCampaign.Runtime.RcText.T("恢复默认地形")};tool="Place";
                }
                listTop=136;
            }
            Widgets.BeginScrollView(new Rect(0,listTop,r.width,r.height-listTop),ref assetsScroll,new Rect(0,0,r.width-18,list.Count*40));
            int first=Math.Max(0,(int)(assetsScroll.y/40)-1),last=Math.Min(list.Count,first+(int)(r.height/40)+4);
            for(int i=first;i<last;i++){
                var d=list[i];var row=new Rect(0,i*40,r.width-22,36);
                if(d is ThingDef thing)Widgets.DefIcon(new Rect(0,row.y,32,32),thing);
                if(EditorUI.Button(new Rect(36,row.y,row.width-36,36),d.LabelCap)){
                    EditorFocus.Release();
                    brush=new Placement{Kind=category=="Floor"?"Terrain":category,Def=Profiles.Ref(d),Name=d.LabelCap,Layer=category=="Thing"?"Objects":category};tool="Place";
                    if(d is ThingDef td&&td.MadeFromStuff){var selectedBrush=brush;selectedBrush.Stuff=Profiles.Ref(GenStuff.DefaultStuffFor(td));EditorUI.PickStuff(td,stuff=>selectedBrush.Stuff=stuff);}
                }
            }Widgets.EndScrollView();GUI.EndGroup();
        }
        IEnumerable<Named> SceneItems()=>Map.Objects.Cast<Named>().Concat(Map.Regions).Concat(Map.Rules).Concat(Map.Objectives).Concat(Map.Hud).Concat(Map.Interactions).Concat(Map.PatrolRoutes).Concat(Map.Stealth.Routes);
        void EditActions(string name) {
            var copy=Json.Copy(Map);Find.WindowStack.Add(new ActionsWindow(name=="Entry"?copy.Entry:copy.Defeat,Project,()=>{
                Edit(_=>{if(name=="Entry")Map.Entry=copy.Entry;else Map.Defeat=copy.Defeat;});
            }));
        }
        void EditObject(Named obj)=>Find.WindowStack.Add(new ObjectWindow(obj,value=>Edit(_=>{
            if(obj is Placement){PlacementStorage.Validate((Placement)value);Map.Objects[Map.Objects.FindIndex(x=>x.Id==obj.Id)]=(Placement)value;}
            else if(obj is Region)Map.Regions[Map.Regions.FindIndex(x=>x.Id==obj.Id)]=(Region)value;
            else if(obj is Rule)Map.Rules[Map.Rules.FindIndex(x=>x.Id==obj.Id)]=(Rule)value;
            else if(obj is Objective)Map.Objectives[Map.Objectives.FindIndex(x=>x.Id==obj.Id)]=(Objective)value;
            else if(obj is HudWidget)Map.Hud[Map.Hud.FindIndex(x=>x.Id==obj.Id)]=(HudWidget)value;
        }),Project,Map));
        void DrawMap(Rect r) {
            mapView.Configure(Map.Id,Map.Width,Map.Height,r.width,r.height);
            var minimap=MinimapPanel(r);
            Vector2 localMouse=Event.current.mousePosition-r.position;
            bool overMinimap=minimap.Contains(Event.current.mousePosition);
            if(Event.current.type==EventType.Repaint){
            EnsureSceneIndex();
            Widgets.DrawBoxSolid(r,new Color(0.08f,0.10f,0.12f));
            GUI.BeginGroup(r);
            var size=new Rect(canvasOffset.x,canvasOffset.y,Map.Width*zoom,Map.Height*zoom);
            Widgets.DrawBoxSolid(size,new Color(0.27f,0.30f,0.22f));
            DrawTerrainPreview(size,new Vector2(r.width,r.height));
            EnsureGraphicLinks();
            foreach(var p in drawableObjects){
                if(p.Kind=="Terrain")continue;
                if(p.Kind=="Roof")continue;
                if(!showAux&&p.Kind=="Marker")continue;
                var at=new Rect(canvasOffset.x+p.Position.X*zoom,canvasOffset.y+(Map.Height-1-p.Position.Z)*zoom,zoom,zoom);
                if(at.xMax < -zoom*4||at.yMax < -zoom*4||at.x>r.width+zoom*4||at.y>r.height+zoom*4)continue;
                if(p.Kind=="Thing") {
                    var d=DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name);
                    if(d!=null) {var bounds=GenAdj.OccupiedRect(new IntVec3(p.Position.X,0,p.Position.Z),new Rot4(p.Rotation),d.size);
                        at=new Rect(canvasOffset.x+bounds.minX*zoom,canvasOffset.y+(Map.Height-1-bounds.maxZ)*zoom,bounds.Width*zoom,bounds.Height*zoom);
                        DrawThingGraphic(p,d,at);}
                }else if(p.Kind=="Boss"||p.Kind=="PawnKind"||p.Kind=="Pawn") {
                    DrawActorMarker(p,at);
                }else Widgets.DrawBoxSolid(at,p.Kind=="Pawn"?Color.cyan:p.Kind=="Marker"?Color.yellow:p.Kind=="Roof"?new Color(.5f,.5f,.7f):TerrainColor(p.Def.Name));
                if(IsSelected(p.Id))SelectionBorder(at);
            }
            DrawHullPreview(r.width,r.height);
            if(showRoofs)foreach(var roof in roofObjects){
                var at=new Rect(canvasOffset.x+roof.Position.X*zoom,canvasOffset.y+(Map.Height-1-roof.Position.Z)*zoom,zoom,zoom);
                if(at.xMax<0||at.yMax<0||at.x>r.width||at.y>r.height)continue;
                Widgets.DrawBoxSolid(at,roof.Def.Name=="RoofRockThick"?new Color(.35f,.25f,.65f,.5f):new Color(.5f,.65f,.9f,.3f));
                if(IsSelected(roof.Id))SelectionBorder(at);
            }
            if(brush?.Kind=="Thing"&&!overMinimap&&size.Contains(localMouse)&&new Rect(0,0,r.width,r.height).Contains(localMouse)){
                var def=DefDatabase<ThingDef>.GetNamedSilentFail(brush.Def.Name);
                if(def!=null){
                    var ghost=new Placement{Id=brush.Id,GraphicIndex=brush.GraphicIndex,Def=brush.Def,Stuff=brush.Stuff,Rotation=brush.Rotation,Position=new Cell{X=Mathf.FloorToInt((localMouse.x-canvasOffset.x)/zoom),Z=Map.Height-1-Mathf.FloorToInt((localMouse.y-canvasOffset.y)/zoom)}};
                    var bounds=GenAdj.OccupiedRect(new IntVec3(ghost.Position.X,0,ghost.Position.Z),new Rot4(ghost.Rotation),def.size);
                    var at=new Rect(canvasOffset.x+bounds.minX*zoom,canvasOffset.y+(Map.Height-1-bounds.maxZ)*zoom,bounds.Width*zoom,bounds.Height*zoom);
                    DrawThingGraphic(ghost,def,at,.65f);SelectionBorder(at);
                }
            }
            DrawStructureReferences();
            if(showAux)DrawTriggerOverlay();
            if(showAux)foreach(var region in Map.Regions){
                var box=new Rect(canvasOffset.x+region.Min.X*zoom,canvasOffset.y+(Map.Height-1-region.Max.Z)*zoom,(region.Max.X-region.Min.X+1)*zoom,(region.Max.Z-region.Min.Z+1)*zoom);
                Widgets.DrawBoxSolid(box,new Color(.2f,.5f,.9f,.22f));EditorUI.DrawLabel(box,region.Name);if(IsSelected(region.Id))SelectionBorder(box);
            }
            DrawSelectionBox();
            DrawSurfaceStroke();
            // GUI group coordinates: require both the visible canvas and actual map bounds.
            if(showAux)DrawStealthOverlay();
            if(showAux)DrawInteractionOverlay();
            var mouse=localMouse;
            bool selected=brush!=null||tool=="Pawn"||tool=="Marker"||tool=="Region"||tool=="Trigger"||selection.Count>0||FindPlacement(selectedId)!=null;
            if(zoom>=6&&selected&&!overMinimap&&!minimapDragging&&new Rect(0,0,r.width,r.height).Contains(mouse)&&size.Contains(mouse)) {
                int cx=Mathf.FloorToInt((mouse.x-canvasOffset.x)/zoom),cy=Mathf.FloorToInt((mouse.y-canvasOffset.y)/zoom);
                int minX=Math.Max(0,cx-4),maxX=Math.Min(Map.Width,cx+5),minY=Math.Max(0,cy-4),maxY=Math.Min(Map.Height,cy+5);
                var tint=new Color(.8f,.88f,1f,.23f);
                for(int x=minX;x<=maxX;x++)Widgets.DrawLine(new Vector2(canvasOffset.x+x*zoom,canvasOffset.y+minY*zoom),new Vector2(canvasOffset.x+x*zoom,canvasOffset.y+maxY*zoom),tint,1);
                for(int y=minY;y<=maxY;y++)Widgets.DrawLine(new Vector2(canvasOffset.x+minX*zoom,canvasOffset.y+y*zoom),new Vector2(canvasOffset.x+maxX*zoom,canvasOffset.y+y*zoom),tint,1);
            }
            GUI.EndGroup();
            }
            bool minimapInput=DrawMinimap(minimap);
            if(!minimapInput)MapInput(r);
        }
        bool IsSelected(string id)=>selection.Contains(id)||selectedId==id;
        static readonly Color SelectionColor=new Color(.2f,.95f,1f,1f);
        static void SelectionBorder(Rect rect) {
            var old=GUI.color;GUI.color=SelectionColor;Widgets.DrawBox(rect.ExpandedBy(2),2);GUI.color=old;
        }
        static void IconOutline(Rect rect,ThingDef def) {
            var old=GUI.color;GUI.color=SelectionColor;
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)if(x!=0||y!=0)
                Widgets.DefIcon(new Rect(rect.x+x*2,rect.y+y*2,rect.width,rect.height),def);
            GUI.color=old;
        }
        static bool HitPlacement(Placement p,Cell cell) {
            var def=p.Kind=="Thing"?DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name):null;
            if(def==null)return p.Position.X==cell.X&&p.Position.Z==cell.Z;
            return GenAdj.OccupiedRect(new IntVec3(p.Position.X,0,p.Position.Z),new Rot4(p.Rotation),def.size).Contains(new IntVec3(cell.X,0,cell.Z));
        }
        static Color TerrainColor(string name)=>name=="Space"?Color.black:name.IndexOf("Sand",StringComparison.OrdinalIgnoreCase)>=0?new Color(.68f,.58f,.37f):name.IndexOf("Ice",StringComparison.OrdinalIgnoreCase)>=0?new Color(.8f,.86f,.9f):name=="SoilRich"?new Color(.29f,.25f,.17f):name.IndexOf("Water",StringComparison.OrdinalIgnoreCase)>=0?new Color(.15f,.35f,.65f):name.IndexOf("Rock",StringComparison.OrdinalIgnoreCase)>=0?Color.gray:new Color(.45f,.37f,.22f);
        Cell CellAt(Vector2 point,Rect r)=>new Cell{X=Mathf.FloorToInt((point.x-r.x-canvasOffset.x)/zoom),Z=Map.Height-1-Mathf.FloorToInt((point.y-r.y-canvasOffset.y)/zoom)};
        void MapInput(Rect r){
            if(!Application.isFocused)return;
            HandleMapInput(r,Event.current);
        }
        void HandleMapInput(Rect r,Event e){
            if(surfacePainting){
                if((e.type==EventType.MouseUp||e.rawType==EventType.MouseUp)&&e.button==0){UpdateSurfaceStroke(CellAt(e.mousePosition,r));CommitSurfaceStroke();e.Use();}
                else if(e.type==EventType.MouseDrag&&e.button==0){UpdateSurfaceStroke(CellAt(e.mousePosition,r));e.Use();}
                else if(e.type==EventType.MouseDown&&e.button==1){CancelSurfaceStroke();e.Use();}
                return;
            }
            if(boxSelecting){
                boxEnd=CellAt(e.mousePosition,r);
                if((e.type==EventType.MouseUp||e.rawType==EventType.MouseUp)&&e.button==0){SelectBox(boxStart,boxEnd);boxSelecting=false;e.Use();}
                else if(e.type==EventType.MouseDown&&e.button==1){boxSelecting=false;e.Use();}
                else if(e.type==EventType.MouseDrag){e.Use();}
                return;
            }
            if(painting) {
                if((e.type==EventType.MouseUp||e.rawType==EventType.MouseUp)&&e.button==0){FinishPaint();e.Use();return;}
                if(e.type==EventType.MouseDrag&&e.button==0){
                    var raw=CellAt(e.mousePosition,r);
                    bool inside=r.Contains(e.mousePosition)&&raw.X>=0&&raw.Z>=0&&raw.X<Map.Width&&raw.Z<Map.Height;
                    var mouse=new Vector2(Mathf.Clamp(e.mousePosition.x,r.x,r.xMax-1),Mathf.Clamp(e.mousePosition.y,r.y,r.yMax-1));
                    if(inside||paintLast!=null)PaintTo(CellAt(mouse,r));
                    if(!inside)paintLast=null;
                    e.Use();return;
                }
                if(e.type==EventType.MouseDown&&e.button!=0)FinishPaint();
                else if(e.type==EventType.ScrollWheel){e.Use();return;}
            }
            if(panning) {
                if((e.type==EventType.MouseUp||e.rawType==EventType.MouseUp)&&e.button==panButton) {
                    if(!panMoved&&panButton==1){tool="";brush=null;}
                    panning=false;e.Use();return;
                }
                if(e.type==EventType.MouseDrag&&e.button==panButton) {
                    if(!panMoved&&(e.mousePosition-panStart).sqrMagnitude>=9f)panMoved=true;
                    if(panMoved){canvasOffset+=e.mousePosition-panLast;panLast=e.mousePosition;}
                    e.Use();return;
                }
                return;
            }
            if(!r.Contains(e.mousePosition)&&!dragging)return;
            if(e.type==EventType.MouseDown&&(e.button==1||e.button==2)&&!dragging) {
                panning=true;panMoved=false;panButton=e.button;panStart=panLast=e.mousePosition;e.Use();return;
            }
            if(e.type==EventType.ScrollWheel){mapView.ZoomAt(e.delta.y,e.mousePosition.x-r.x,e.mousePosition.y-r.y);e.Use();return;}
            var cell=CellAt(e.mousePosition,r);
            if(e.type==EventType.MouseDown&&e.button==0&&cell.X>=0&&cell.Z>=0&&cell.X<Map.Width&&cell.Z<Map.Height){
                bool additive=(e.modifiers&EventModifiers.Shift)!=0;
                EditorFocus.Release();
                if(additive&&tool!="Region"){FinishPaint();tool="";brush=null;}
                if(tool=="PatrolPoint"){AddPatrolPoint(cell);e.Use();return;}
                if(tool=="Place"&&brush!=null){if(SurfaceTool(brush))BeginSurfaceStroke(brush,cell);else BeginPaint(brush,cell);e.Use();return;}
                if(tool=="Pawn"||tool=="Marker"){
                    BeginPaint(new Placement{Name=tool,Kind=tool,Role=tool=="Pawn"?Map.Roles.FirstOrDefault()??"hero":"",Layer="Auxiliary"},cell);e.Use();return;
                }
                if(tool=="Trigger"){AddTrigger(cell);e.Use();return;}
                if(tool=="Region"){origin=cell;dragging=true;dragStart=e.mousePosition;e.Use();return;}
                var hit=PickEditable(cell);
                if(hit!=null){
                    SelectPlacement(hit,additive);
                    if(e.clickCount==2&&!additive&&selection.Count==1){InspectSelection(hit.Id);e.Use();return;}
                    BeginSelectionMove(cell);
                }else{
                    if(!additive){selection.Clear();selectedId="";}
                    boxSelecting=true;boxStart=boxEnd=cell;
                }
                e.Use();
            }else if(e.type==EventType.MouseDrag&&e.button==0&&dragging&&tool!="Region"){
                PreviewSelectionMove(cell);e.Use();
            }else if((e.type==EventType.MouseUp||e.rawType==EventType.MouseUp)&&e.button==0&&dragging){
                dragging=false;
                if(tool=="Region")Edit(_=>Map.Regions.Add(new Region{Name=RimCampaign.Runtime.RcText.T("区域"),Min=new Cell{X=Math.Max(0,Math.Min(origin.X,cell.X)),Z=Math.Max(0,Math.Min(origin.Z,cell.Z))},Max=new Cell{X=Math.Min(Map.Width-1,Math.Max(origin.X,cell.X)),Z=Math.Min(Map.Height-1,Math.Max(origin.Z,cell.Z))}}));
                else FinishSelectionMove();e.Use();
            }
        }
        void BeginPaint(Placement template,Cell cell) {
            painting=true;paintSnapshot=Json.Write(Project);paintBrush=Json.Copy(template);painted.Clear();paintLast=null;selection.Clear();PaintTo(cell);
        }
        void FinishPaint() {
            if(!painting)return;
            painting=false;history.CommitAppliedChange(paintSnapshot);paintSnapshot=null;paintBrush=null;paintLast=null;painted.Clear();
        }
        void CancelPointerGestures(){
            FinishPaint();CancelSurfaceStroke();
            if(dragging&&tool!="Region")foreach(var pair in originalPositions)SetEditablePosition(pair.Key,Json.Copy(pair.Value));
            originalPositions.Clear();dragging=false;selectionMoved=false;toggleOnRelease=null;boxSelecting=false;panning=false;minimapDragging=false;dragLeft=dragRight=false;
            InvalidateScene();GUIUtility.hotControl=0;EditorFocus.Release();
        }
        public override void PostClose(){FinishPaint();CancelSurfaceStroke();if(dragging&&tool!="Region")FinishSelectionMove();if(terrainPreview!=null)UnityEngine.Object.Destroy(terrainPreview);if(minimapTexture!=null)UnityEngine.Object.Destroy(minimapTexture);terrainPreview=null;minimapTexture=null;InvalidateScene();minimapRevision=-1;base.PostClose();}
        void PaintTo(Cell target) {
            int x=Mathf.Clamp(target.X,0,Map.Width-1),z=Mathf.Clamp(target.Z,0,Map.Height-1);
            int startX=paintLast?.X??x,startZ=paintLast?.Z??z;
            int dx=Math.Abs(x-startX),dz=Math.Abs(z-startZ),sx=startX<x?1:-1,sz=startZ<z?1:-1,error=dx-dz;
            while(true){
                PaintCell(new Cell{X=startX,Z=startZ});if(startX==x&&startZ==z)break;
                int twice=error*2;if(twice>-dz){error-=dz;startX+=sx;}if(twice<dx){error+=dx;startZ+=sz;}
            }
            paintLast=new Cell{X=x,Z=z};
        }
        void PaintCell(Cell cell) {
            if(!painted.Add(cell.Z*Map.Width+cell.X))return;
            var p=paintBrush;
            if(SurfaceTool(p)){TerrainSurfaces.Apply(Map,p,new[]{cell});InvalidateScene();return;}
            if(p.Kind=="EraseRoof"){
                TerrainInitializer.EraseSurface(Map,cell,true);InvalidateScene();return;
            }
            if(Map.Objects.Any(o=>!TerrainInitializer.Generated(o)&&o.Kind==p.Kind&&o.Position.X==cell.X&&o.Position.Z==cell.Z&&o.Def?.Name==p.Def?.Name&&o.Rotation==p.Rotation&&o.Faction==p.Faction&&o.Boss==p.Boss&&o.Role==p.Role&&o.Stuff?.Name==p.Stuff?.Name))return;
            var def=p.Kind=="Thing"?DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name):null;
            if(def!=null) {
                var bounds=GenAdj.OccupiedRect(new IntVec3(cell.X,0,cell.Z),new Rot4(p.Rotation),def.size);
                if(bounds.minX<0||bounds.minZ<0||bounds.maxX>=Map.Width||bounds.maxZ>=Map.Height)return;
                if(def.category==ThingCategory.Building)foreach(var other in Map.Objects.Where(o=>o.Kind=="Thing")) {
                    var otherDef=DefDatabase<ThingDef>.GetNamedSilentFail(other.Def.Name);if(otherDef?.category!=ThingCategory.Building||TerrainInitializer.NaturalObject(other))continue;
                    var occupied=GenAdj.OccupiedRect(new IntVec3(other.Position.X,0,other.Position.Z),new Rot4(other.Rotation),otherDef.size);
                    if(bounds.minX<=occupied.maxX&&bounds.maxX>=occupied.minX&&bounds.minZ<=occupied.maxZ&&bounds.maxZ>=occupied.minZ&&!CanShareBuildingCell(def,otherDef))return;
                }
            }
            var placed=Json.Copy(p);placed.Id=Guid.NewGuid().ToString("N");placed.Position=cell;
            if(def!=null)PlacementGraphics.FixNewPlacement(placed,def);
            if(p.Kind=="Terrain"||p.Kind=="Roof")Map.Objects.RemoveAll(o=>o.Layer!=TerrainInitializer.Layer&&o.Kind==p.Kind&&o.Position.X==cell.X&&o.Position.Z==cell.Z);
            if(placed.Kind!="Roof"&&!TerrainInitializer.NaturalObject(placed))TerrainInitializer.ClearNatureAt(Map,TerrainInitializer.Footprint(placed));
            Map.Objects.Add(placed);selectedId=placed.Id;
            InvalidateScene();
            var deviceKind=DeviceSpec.KindForDef(placed.Def?.Name);
            if(deviceKind!=null)Map.Stealth.Devices.Add(new DeviceSpec{Name=placed.Name,Kind=deviceKind,Host=placed.Id,Position=StealthMath.Copy(cell),Facing=placed.Rotation});
        }
        void AddTrigger(Cell cell){
            var rule=new Rule{Name=RimCampaign.Runtime.RcText.T("空间触发器 ")+(Map.Rules.Count+1),Position=Json.Copy(cell),Events=new List<EventSpec>{new EventSpec{Kind="RangeEnter"}}};
            Edit(_=>Map.Rules.Add(rule));selection.Clear();selection.Add(rule.Id);selectedId=rule.Id;tool="";brush=null;
            status=RimCampaign.Runtime.RcText.T("触发器已放置；拖动移动，在右侧配置范围、检测对象与动作。");
        }
        List<Rule> ruleClipboard=new List<Rule>();
        void Copy(){clipboard=Json.Copy(SelectedPlacements());ruleClipboard=Json.Copy(Map.Rules.Where(p=>IsSelected(p.Id)).ToList());status=RimCampaign.Runtime.RcText.T("已复制 ")+(clipboard.Count+ruleClipboard.Count)+RimCampaign.Runtime.RcText.T(" 个对象");}
        static bool CanShareBuildingCell(ThingDef first,ThingDef second)=>
            (first.building?.isPowerConduit==true||second.building?.isPowerConduit==true)&&
            GenConstruct.CanPlaceBlueprintOver(first,second)&&GenConstruct.CanPlaceBlueprintOver(second,first)&&
            !GenSpawn.SpawningWipes(first,second)&&!GenSpawn.SpawningWipes(second,first);
        void Paste(){
            var copies=History<Project>.CopyObjects(clipboard,1,1);var rules=ruleClipboard.Select(r=>TriggerGeometry.Copy(r,1,1)).ToList();
            if(copies.Select(PlacementBounds).Any(b=>b.minX<0||b.minZ<0||b.maxX>=Map.Width||b.maxZ>=Map.Height)||rules.Any(r=>r.Position.X>=Map.Width||r.Position.Z>=Map.Height)){status=RimCampaign.Runtime.RcText.T("粘贴会超出地图边界，请先移动原对象。");return;}
            var ids=clipboard.Select((o,i)=>new {o.Id,Next=copies[i].Id}).Concat(ruleClipboard.Select((o,i)=>new{o.Id,Next=rules[i].Id})).ToDictionary(x=>x.Id,x=>x.Next);
            foreach(var rule in rules){if(ids.ContainsKey(rule.Host))rule.Host=ids[rule.Host];if(ids.ContainsKey(rule.Subject))rule.Subject=ids[rule.Subject];foreach(var e in rule.Events)if(ids.ContainsKey(e.Target))e.Target=ids[e.Target];foreach(var a in Validation.Actions(rule.Actions))if(ids.ContainsKey(a.Target))a.Target=ids[a.Target];}
            EditPlacements(copies,rules.Select(r=>r.Id),()=>{Map.Objects.AddRange(copies);Map.Rules.AddRange(rules);selection=new HashSet<string>(copies.Select(p=>p.Id).Concat(rules.Select(p=>p.Id)));selectedId=selection.LastOrDefault()??"";},true);
        }
        void Delete(){
            var ids=selection.Count>0?selection:new HashSet<string>{selectedId};
            var uses=Validation.Actions(Map.Rules.SelectMany(x=>x.Actions).Concat(Map.Entry).Concat(Map.Defeat)).Where(a=>ids.Contains(a.Target)||ids.Contains(a.Text)).Select(a=>EditorUI.Label(a)).ToList();
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(RimCampaign.Runtime.RcText.T("删除所选对象？引用将需要重新绑定。\n")+string.Join("\n",uses),()=>RemoveSelection(ids)));
        }
        void RemoveSelection(HashSet<string> ids){Edit(_=>{
                RemovePatrolReferences(ids);
                Map.Entry.RemoveAll(a=>a.Kind=="rc.spawnStructure"&&ids.Contains(a.Origin));
                Map.Objects.RemoveAll(o=>ids.Contains(o.Id));Map.Rules.RemoveAll(o=>ids.Contains(o.Id));Map.Regions.RemoveAll(o=>ids.Contains(o.Id));Map.Objectives.RemoveAll(o=>ids.Contains(o.Id));Map.Hud.RemoveAll(o=>ids.Contains(o.Id));selection.Clear();selectedId="";
                Map.Interactions.RemoveAll(o=>ids.Contains(o.Id)||ids.Contains(o.Host));
            });
        }
        void HandleKeys(){
            var e=Event.current;if(e.type!=EventType.KeyDown||EditorFocus.Typing)return;
            if(surfacePainting){if(e.keyCode==KeyCode.Escape){CancelSurfaceStroke();e.Use();}return;}
            FinishPaint();
            if(dragging&&tool!="Region")FinishSelectionMove();
            if(boxSelecting){if(e.keyCode==KeyCode.Escape){boxSelecting=false;e.Use();}return;}
            if(e.control&&e.keyCode==KeyCode.S){Save();e.Use();}
            if(e.control&&e.keyCode==KeyCode.Z){UndoEdit(false);e.Use();}
            if(e.control&&e.keyCode==KeyCode.Y){UndoEdit(true);e.Use();}
            if(e.control&&e.keyCode==KeyCode.C){Copy();e.Use();}
            if(e.control&&e.keyCode==KeyCode.V){Paste();e.Use();}
            if(e.keyCode==KeyCode.Delete){Delete();e.Use();}
            if(e.keyCode==KeyCode.R){if(brush!=null)brush.Rotation=(brush.Rotation+1)%4;else RotateSelection(e.shift);e.Use();}
        }
        void Resize(Rect r,Rect center){
            if(!Application.isFocused)return;
            var e=Event.current;
            var l=new Rect(left,84,7,r.height-118);var rr=new Rect(r.width-right-7,84,7,r.height-118);
            if(e.type==EventType.MouseDown&&e.button==0){dragLeft=leftOpen&&l.Contains(e.mousePosition);dragRight=rightOpen&&rr.Contains(e.mousePosition);}
            if(e.type==EventType.MouseDrag){if(dragLeft){left=Mathf.Clamp(e.mousePosition.x,160,r.width*.35f);e.Use();}if(dragRight){right=Mathf.Clamp(r.width-e.mousePosition.x,190,r.width*.4f);e.Use();}}
            if(e.type==EventType.MouseUp){dragLeft=dragRight=false;}
        }
        void GenerateBase()=>Find.WindowStack.Add(new TerrainInitializationWindow(Map.TerrainInitialization,settings=>{
            Edit(_=>TerrainInitializer.Initialize(Map,settings));
            selection.Clear();selectedId="";patrolRoute="";tool="";brush=null;
            status=RimCampaign.Runtime.RcText.T("旧布置已清空，地形、植被与山体已初始化。可撤销恢复。");
        }));
        string graphNode="";bool nodeDragging;Vector2 nodeStart,mouseStart;
        void DrawGraph(Rect r){
            if(Project.Campaigns.Count==0){if(EditorUI.Button(r,RimCampaign.Runtime.RcText.T("创建战役")))Edit(p=>p.Campaigns.Add(new Campaign{Name=p.Name}));return;}
            var c=Project.Campaigns[0];Widgets.DrawBoxSolid(r,new Color(.09f,.11f,.13f));GUI.BeginGroup(r);
            if(EditorUI.Button(new Rect(0,0,100,30),RimCampaign.Runtime.RcText.T("+ 关卡")))Edit(_=>{var n=new Node{Name=RimCampaign.Runtime.RcText.T("关卡 ")+(c.Nodes.Count+1),Map=mapId,X=50+c.Nodes.Count*35,Y=80+c.Nodes.Count*35};c.Nodes.Add(n);if(c.Start=="")c.Start=n.Id;});
            if(EditorUI.Button(new Rect(105,0,100,30),RimCampaign.Runtime.RcText.T("+ 连接")))Find.WindowStack.Add(new ObjectWindow(new Edge{From=graphNode},o=>Edit(_=>c.Edges.Add((Edge)o)),Project));
            if(EditorUI.Button(new Rect(210,0,120,30),RimCampaign.Runtime.RcText.T("战役设置")))Find.WindowStack.Add(new ObjectWindow(c,o=>Edit(p=>p.Campaigns[0]=(Campaign)o),Project));
            foreach(var edge in c.Edges){
                var from=c.Nodes.FirstOrDefault(n=>n.Id==edge.From);var to=c.Nodes.FirstOrDefault(n=>n.Id==edge.To);if(from==null||to==null)continue;
                var a=new Vector2(from.X+160,from.Y+35)+graphOffset;var b=new Vector2(to.X,to.Y+35)+graphOffset;
                Widgets.DrawLine(a,b,Color.gray,2);
                if(EditorUI.Button(new Rect((a.x+b.x)/2-65,(a.y+b.y)/2-13,130,26),edge.Result+" / "+edge.When.Op))Find.WindowStack.Add(new ObjectWindow(edge,o=>Edit(_=>c.Edges[c.Edges.IndexOf(edge)]=(Edge)o),Project));
            }
            foreach(var n in c.Nodes){
                var box=new Rect(n.X+graphOffset.x,n.Y+graphOffset.y,160,72);Widgets.DrawWindowBackground(box);
                EditorUI.DrawLabel(box.ContractedBy(5),(c.Start==n.Id?"▶ ":"")+n.Name+"\n"+n.Kind);
                var e=Event.current;
                if(e.type==EventType.MouseDown&&box.Contains(e.mousePosition)){
                    graphNode=n.Id;
                    if(e.button==1){Find.WindowStack.Add(new ObjectWindow(n,o=>Edit(_=>c.Nodes[c.Nodes.IndexOf(n)]=(Node)o),Project));e.Use();}
                    else if(e.clickCount==2&&Project.Maps.Any(m=>m.Id==n.Map)){mapId=n.Map;tab="Map";e.Use();}
                    else{nodeDragging=true;nodeStart=new Vector2(n.X,n.Y);mouseStart=e.mousePosition;e.Use();}
                }
            }
            var ev=Event.current;
            if(nodeDragging&&ev.type==EventType.MouseDrag){var n=c.Nodes.Single(x=>x.Id==graphNode);n.X=nodeStart.x+ev.mousePosition.x-mouseStart.x;n.Y=nodeStart.y+ev.mousePosition.y-mouseStart.y;ev.Use();}
            if(nodeDragging&&ev.type==EventType.MouseUp){nodeDragging=false;var n=c.Nodes.Single(x=>x.Id==graphNode);var end=new Vector2(n.X,n.Y);n.X=nodeStart.x;n.Y=nodeStart.y;Edit(_=>{n.X=end.x;n.Y=end.y;});ev.Use();}
            if(ev.type==EventType.MouseDrag&&ev.button==2){graphOffset+=ev.delta;ev.Use();}
            GUI.EndGroup();
        }
    }
    public class MapSizeWindow : Window
    {
        string width,height,error="";readonly Func<int,int,string> apply;
        public override Vector2 InitialSize=>new Vector2(520,330);
        public MapSizeWindow(int width,int height,Func<int,int,string> apply) {
            this.width=width.ToString();this.height=height.ToString();this.apply=apply;
            doCloseX=true;absorbInputAroundWindow=true;forcePause=true;
        }
        public override void DoWindowContents(Rect r) {
            EditorUI.DrawLabel(new Rect(0,0,r.width-40,32),RimCampaign.Runtime.RcText.T("地图尺寸（格）"));
            EditorUI.DrawLabel(new Rect(0,44,60,32),RimCampaign.Runtime.RcText.T("宽 X"));width=Widgets.TextField(new Rect(65,44,130,32),width);
            EditorUI.DrawLabel(new Rect(235,44,60,32),RimCampaign.Runtime.RcText.T("高 Z"));height=Widgets.TextField(new Rect(300,44,130,32),height);
            EditorUI.DrawLabel(new Rect(0,92,r.width,62),RimCampaign.Runtime.RcText.T("范围：20–500。已有内容保持原坐标。\n缩小前请移走边界外的内容；修改支持撤销。"));
            EditorUI.DrawLabel(new Rect(0,158,r.width,85),error);
            if(EditorUI.Button(new Rect(0,r.height-38,r.width,36),RimCampaign.Runtime.RcText.T("应用尺寸"))) {
                if(!int.TryParse(width,out int w)||!int.TryParse(height,out int h))error=RimCampaign.Runtime.RcText.T("请输入整数。");
                else {error=apply(w,h)??"";if(error=="")Close();}
            }
        }
    }
    public class TextPrompt : Window
    {
        readonly string title;string value,error="";readonly Action<string> done;public override Vector2 InitialSize=>new Vector2(760,310);
        public TextPrompt(string title,string value,Action<string> done){this.title=title;this.value=value;this.done=done;doCloseX=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect r){
            EditorUI.DrawLabel(new Rect(0,0,r.width,30),title);value=Widgets.TextField(new Rect(0,40,r.width,32),value);
            EditorUI.DrawLabel(new Rect(0,82,r.width,135),error);
            if(EditorUI.Button(new Rect(0,r.height-40,r.width,35),RimCampaign.Runtime.RcText.T("确认"))){try{done(value);Close();}catch(Exception ex){error=ex.Message;}}
        }
    }
    public class ActionsWindow : ListWindow
    {
        readonly Action apply;
        public ActionsWindow(List<Act> actions,Project p,Action apply):base(actions,p){this.apply=apply;}
        public override void PostClose(){base.PostClose();apply();}
    }
}
