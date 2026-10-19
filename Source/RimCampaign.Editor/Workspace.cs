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
            if(Current.ProgramState==ProgramState.Entry&&Widgets.ButtonText(new Rect(rect.x-220,rect.y,205,45),"RimCampaign 创作"))Find.WindowStack.Add(new Workspace());
        }
    }
    public class Workspace : Window
    {
        History<Project> history;string mapId,tab="Map",search="",source="",category="Thing",status="",path="";
        Vector2 assetsScroll,sceneScroll,canvasOffset=new Vector2(20,20),graphOffset=new Vector2(20,20);float zoom=7,left=235,right=270;
        bool leftOpen=true,rightOpen=true,dragLeft,dragRight,showAux=true,lockAux;
        Placement brush;string tool="",selectedId="";HashSet<string> selection=new HashSet<string>();
        List<Placement> clipboard=new List<Placement>();Vector2 dragStart;Cell origin;bool dragging;string dragSnapshot;
        bool painting;string paintSnapshot;Placement paintBrush;Cell paintLast;readonly HashSet<int> painted=new HashSet<int>();
        bool panning,panMoved;int panButton;Vector2 panStart,panLast;
        Dictionary<string,Cell> originalPositions=new Dictionary<string,Cell>();
        public static string WorkRoot=>Path.Combine(GenFilePaths.SaveDataFolderPath,"RimCampaign");
        public Project Project=>history.Value;
        public MapSpec Map=>Project.Maps.FirstOrDefault(m=>m.Id==mapId)??Project.Maps.First();
        public override Vector2 InitialSize=>new Vector2(UI.screenWidth-24,UI.screenHeight-24);
        public Workspace() {
            forcePause=true;doCloseX=true;absorbInputAroundWindow=true;preventCameraMotion=true;
            var p=NewProject();history=new History<Project>(p);mapId=p.Maps[0].Id;
        }
        static Project NewProject(){
            var m=new MapSpec{Name="地图 1"};m.Hud.Add(new HudWidget{Kind="Objectives",Name="目标列表",Height=120});
            return new Project{Name="新战役",Maps=new List<MapSpec>{m}};
        }
        void Edit(Action<Project> change){history.Edit(change);}
        public override void DoWindowContents(Rect r) {
            try{
                Toolbar(r);
                float lx=leftOpen?left:0,rx=rightOpen?right:0;
                var center=new Rect(lx+8,84,r.width-lx-rx-16,r.height-118);
                if(leftOpen)Library(new Rect(0,84,left,r.height-118));
                if(rightOpen)Properties(new Rect(r.width-right,84,right,r.height-118));
                if(tab=="Map")DrawMap(center);else DrawGraph(center);
                Resize(r,center);
                Widgets.Label(new Rect(0,r.height-29,r.width,29),status);
                HandleKeys();
            }catch(Exception ex){status=ex.GetBaseException().Message;Log.ErrorOnce("[RimCampaign editor] "+ex,ex.GetType().GetHashCode());}
        }
        void Toolbar(Rect r) {
            float x=0;
            void B(string label,float width,Action a){if(Widgets.ButtonText(new Rect(x,0,width,32),label))a();x+=width+4;}
            B("新建",60,()=>{history=new History<Project>(NewProject());mapId=Project.Maps[0].Id;path="";});
            B("打开",60,()=>Prompt("项目 JSON 路径",path,s=>{var p=Json.Load<Project>(s);history=new History<Project>(p);mapId=p.Maps.First().Id;path=s;}));
            B("保存",60,Save);
            B("导出",60,Export);
            B("撤销",60,()=>history.Undo());B("重做",60,()=>history.Redo());
            B("项目设置",90,()=>Find.WindowStack.Add(new ObjectWindow(Project,o=>history.Replace((Project)o),Project)));
            B("角色档案",90,()=>Find.WindowStack.Add(new ProfilesWindow(Project,p=>Edit(x=>x.Profiles=p))));
            B("校验",60,Validate);
            B("C# 扩展",85,()=>Prompt("C# 文件路径", "",s=>Compile(s)));
            B("预览",60,()=>Find.WindowStack.Add(new PreviewWindow(Map)));
            if(Widgets.ButtonText(new Rect(0,40,95,30),leftOpen?"◀ 素材":"▶ 素材"))leftOpen=!leftOpen;
            if(Widgets.ButtonText(new Rect(100,40,95,30),rightOpen?"属性 ▶":"属性 ◀"))rightOpen=!rightOpen;
            if(Widgets.ButtonText(new Rect(200,40,100,30),tab=="Map"?"切换战役图":"返回地图"))tab=tab=="Map"?"Campaign":"Map";
            if(Widgets.ButtonText(new Rect(305,40,160,30),Map.Name))EditorUI.Pick(Project.Maps.Select(m=>new KeyValuePair<string,string>(m.Id,m.Name)),s=>mapId=s);
            if(Widgets.ButtonText(new Rect(470,40,80,30),"+ 地图"))Edit(p=>{var m=new MapSpec{Name="地图 "+(p.Maps.Count+1)};p.Maps.Add(m);mapId=m.Id;});
            if(Widgets.ButtonText(new Rect(555,40,90,30),"地图设置"))Find.WindowStack.Add(new ObjectWindow(Map,o=>{var draft=(MapSpec)o;var error=MapSizeError(draft,draft.Width,draft.Height);if(error!=null)throw new InvalidOperationException(error);Edit(p=>p.Maps[p.Maps.FindIndex(m=>m.Id==mapId)]=draft);},Project));
            if(Widgets.ButtonText(new Rect(650,40,80,30),"生成底图"))GenerateBase();
            if(Widgets.ButtonText(new Rect(735,40,80,30),showAux?"辅助 ✓":"辅助"))showAux=!showAux;
            if(Widgets.ButtonText(new Rect(820,40,80,30),lockAux?"已锁定":"锁定辅助"))lockAux=!lockAux;
            if(Widgets.ButtonText(new Rect(905,40,155,30),"地图尺寸 "+Map.Width+"×"+Map.Height))
                Find.WindowStack.Add(new MapSizeWindow(Map.Width,Map.Height,TryResizeMap));
            if(Widgets.ButtonText(new Rect(1065,40,90,30),"地图阵营"))
                Find.WindowStack.Add(new MapFactionsWindow(Map,draft=>Edit(p=>p.Maps[p.Maps.FindIndex(m=>m.Id==draft.Id)]=draft)));
        }
        internal string TryResizeMap(int width,int height) {
            var error=MapSizeError(Map,width,height);if(error!=null)return error;
            int oldHeight=Map.Height;
            Edit(_=>{Map.Width=width;Map.Height=height;});
            canvasOffset.y-=(height-oldHeight)*zoom;
            status="地图尺寸已改为 "+width+" × "+height+"；可撤销。";
            return null;
        }
        internal static string MapSizeError(MapSpec map,int width,int height) {
            if(width<20||height<20||width>500||height>500)return "宽和高都必须是 20–500 之间的整数。";
            bool Outside(Cell c)=>c==null||c.X<0||c.Z<0||c.X>=width||c.Z>=height;
            var blocked=new List<string>();
            foreach(var p in map.Objects) {
                bool outside=Outside(p.Position);
                var def=p.Kind=="Thing"?DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name):null;
                if(!outside&&def!=null) {
                    var bounds=GenAdj.OccupiedRect(new IntVec3(p.Position.X,0,p.Position.Z),new Rot4(p.Rotation),def.size);
                    outside=bounds.minX<0||bounds.minZ<0||bounds.maxX>=width||bounds.maxZ>=height;
                }
                if(outside)blocked.Add(p.Name);
            }
            blocked.AddRange(map.Regions.Where(a=>Outside(a.Min)||Outside(a.Max)).Select(a=>a.Name));
            blocked.AddRange(map.Rules.Where(a=>Outside(a.Position)).Select(a=>a.Name));
            return blocked.Count==0?null:"有 "+blocked.Count+" 个对象、区域或触发器会超出边界。请先移动或删除：\n"+string.Join("、",blocked.Take(6));
        }
        void Save() {
            if(path==""){Prompt("保存项目 JSON",Path.Combine(WorkRoot,"Projects",Project.PackageId+".json"),s=>{path=s;Json.Save(path,Project);status="已保存 "+path;});return;}
            Json.Save(path,Project);status="已保存 "+path;
        }
        public static void Prompt(string title,string value,Action<string> done)=>Find.WindowStack.Add(new TextPrompt(title,value,done));
        void Export() {
            var report=Dependencies.Scan(Project,CampaignDef.Catalog());var issues=Validation.Check(Project);
            if(issues.Count>0||report.Errors.Count>0){status="导出校验失败";Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n",issues.Select(i=>i.ToString()).Concat(report.Errors))));return;}
            Prompt("导出父目录",Path.Combine(WorkRoot,"Exports"),s=>{
                var result=Exporter.Export(Project,s,report,path==""?null:Path.GetDirectoryName(path));status="已导出 "+result;
                Find.WindowStack.Add(new Dialog_MessageBox("战役包已生成：\n"+result+"\n\n启用该子模组并重新加载游戏后，在新游戏剧本中选择战役。"));
            });
        }
        void Validate() {
            var issues=Validation.Check(Project);var report=Dependencies.Scan(Project,CampaignDef.Catalog());
            if(issues.Count==0&&report.Errors.Count==0){status="校验通过；直接依赖 "+report.Direct.Count+"，loadAfter "+report.LoadAfter.Count;return;}
            EditorUI.Pick(issues.Select(i=>new KeyValuePair<string,string>(i.Path,i.ToString())),s=>{
                var parts=s.Split('/');if(Project.Maps.Any(m=>m.Id==parts[0])){mapId=parts[0];tab="Map";selectedId=parts.Length>1?parts[1]:"";}status=s;
            });
            if(report.Errors.Count>0)Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n",report.Errors)));
        }
        void Compile(string sourcePath) {
            var output=ExtensionCompiler.Compile(Project,sourcePath);Edit(p=>{p.CustomAssemblies.RemoveAll(a=>Path.GetFileName(a)==Path.GetFileName(output));p.CustomAssemblies.Add(output);});status="编译成功；导出并重新加载模组后生效。";
        }
        void Library(Rect r) {
            GUI.BeginGroup(r);
            search=Widgets.TextField(new Rect(0,0,r.width,29),search);
            if(Widgets.ButtonText(new Rect(0,34,r.width,27),source==""?"全部模组":source))EditorUI.Pick(new[]{""}.Concat(LoadedModManager.RunningMods.Select(m=>m.PackageIdPlayerFacing)),s=>source=s);
            if(Widgets.ButtonText(new Rect(0,65,r.width,27),category))EditorUI.Pick(new[]{"Thing","Terrain","Roof","PawnKind","Pawn","Boss","Marker","Region","Trigger","Scene"},s=>category=s);
            if(category=="Boss") {
                if(Project.Bosses==null)Project.Bosses=new List<BossTemplate>();
                if(Widgets.ButtonText(new Rect(0,100,r.width,32),"+ Boss 模板"))Edit(p=>p.Bosses.Add(new BossTemplate{Name="新 Boss"}));
                Widgets.BeginScrollView(new Rect(0,140,r.width,r.height-140),ref assetsScroll,new Rect(0,0,r.width-20,Project.Bosses.Count*72));
                for(int i=0;i<Project.Bosses.Count;i++) {
                    var boss=Project.Bosses[i];float y=i*72;
                    if(Widgets.ButtonText(new Rect(0,y,r.width-24,32),"放置 · "+boss.Name)){brush=new Placement{Kind="Boss",Boss=boss.Id,Name=boss.Name,Faction="Hostile"};tool="Place";}
                    if(Widgets.ButtonText(new Rect(0,y+34,r.width-24,30),"编辑阶段与行为"))Find.WindowStack.Add(new ObjectWindow(boss,o=>Edit(p=>p.Bosses[p.Bosses.FindIndex(b=>b.Id==boss.Id)]=(BossTemplate)o),Project));
                }
                Widgets.EndScrollView();GUI.EndGroup();return;
            }
            if(category=="Scene") {Scene(new Rect(0,98,r.width,r.height-98));GUI.EndGroup();return;}
            if(new[]{"Pawn","Marker","Region","Trigger"}.Contains(category)) {
                if(Widgets.ButtonText(new Rect(0,105,r.width,35),"放置 "+category)){tool=category;brush=null;}
                GUI.EndGroup();return;
            }
            var type=category=="PawnKind"?"PawnKindDef":category=="Terrain"?"TerrainDef":category=="Roof"?"RoofDef":"ThingDef";
            var list=EditorUI.Defs(type).Where(d=>(source==""||d.modContentPack?.PackageIdPlayerFacing==source)&&
                ((d.label??"").IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0||d.defName.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)).
                Where(d=>!(d is ThingDef t)||t.category!=ThingCategory.Pawn).ToList();
            Widgets.BeginScrollView(new Rect(0,98,r.width,r.height-98),ref assetsScroll,new Rect(0,0,r.width-18,list.Count*40));
            int first=Math.Max(0,(int)(assetsScroll.y/40)-1),last=Math.Min(list.Count,first+(int)(r.height/40)+4);
            for(int i=first;i<last;i++){
                var d=list[i];var row=new Rect(0,i*40,r.width-22,36);
                if(d is ThingDef thing)Widgets.DefIcon(new Rect(0,row.y,32,32),thing);
                if(Widgets.ButtonText(new Rect(36,row.y,row.width-36,36),d.LabelCap)){
                    brush=new Placement{Kind=category,Def=Profiles.Ref(d),Name=d.LabelCap,Layer=category=="Thing"?"Objects":category};tool="Place";
                    if(d is ThingDef td&&td.MadeFromStuff)brush.Stuff=Profiles.Ref(GenStuff.DefaultStuffFor(td));
                }
            }Widgets.EndScrollView();GUI.EndGroup();
        }
        IEnumerable<Named> SceneItems()=>Map.Objects.Cast<Named>().Concat(Map.Regions).Concat(Map.Rules).Concat(Map.Objectives).Concat(Map.Hud);
        void Scene(Rect r){
            var items=SceneItems().Where(x=>x.Name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).ToList();
            Widgets.BeginScrollView(r,ref sceneScroll,new Rect(0,0,r.width-18,items.Count*32));
            for(int i=0;i<items.Count;i++)if(Widgets.ButtonText(new Rect(0,i*32,r.width-22,29),(items[i].Id==selectedId?"● ":"")+items[i].Name)){
                selectedId=items[i].Id;selection.Clear();selection.Add(selectedId);
            }Widgets.EndScrollView();
        }
        void Properties(Rect r) {
            GUI.BeginGroup(r);var selected=SceneItems().FirstOrDefault(o=>o.Id==selectedId);
            Widgets.Label(new Rect(0,0,r.width,40),selected==null?"选择地图对象":selected.Name);
            float y=45;
            void B(string label,Action action){if(Widgets.ButtonText(new Rect(0,y,r.width,33),label))action();y+=39;}
            if(selected!=null) {
                B("编辑属性 / Edit",()=>EditObject(selected));
                B("删除（检查引用）",Delete);
                if(selected is Placement p){
                    B("旋转 R",()=>Edit(_=>p.Rotation=(p.Rotation+1)%4));
                    B("复制 Ctrl+C",Copy);B("保存组合",()=>Prompt("组合文件",Path.Combine(WorkRoot,"Prefabs",p.Name+".json"),s=>Json.Save(s,Map.Objects.Where(o=>selection.Contains(o.Id)).ToList())));
                    Widgets.Label(new Rect(0,y,r.width,80),"X: "+p.Position.X+"  Z: "+p.Position.Z+"\n来源: "+p.Def.Package);y+=80;
                }
            }
            B("+ 触发器",()=>AddTrigger(new Cell{X=Map.Width/2,Z=Map.Height/2}));
            B("+ 任务目标",()=>{var o=new Objective{Name="新目标"};Edit(_=>Map.Objectives.Add(o));EditObject(o);});
            B("+ HUD",()=>{var w=new HudWidget{Name="信息",Text="信息"};Edit(_=>Map.Hud.Add(w));EditObject(w);});
            B("入场演出",()=>EditActions("Entry"));B("失败演出",()=>EditActions("Defeat"));
            B("导入组合",()=>Prompt("组合 JSON 路径","",s=>{clipboard=Json.Load<List<Placement>>(s);Paste();}));
            B("依赖报告",()=>{var d=Dependencies.Scan(Project,CampaignDef.Catalog());Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n",d.Uses.Select(u=>u.Key+" ← "+string.Join(", ",u.Value)))+"\n\nloadAfter:\n"+string.Join("\n",d.LoadAfter)));});
            GUI.EndGroup();
        }
        void EditActions(string name) {
            var copy=Json.Copy(Map);Find.WindowStack.Add(new ActionsWindow(name=="Entry"?copy.Entry:copy.Defeat,Project,()=>{
                Edit(_=>{if(name=="Entry")Map.Entry=copy.Entry;else Map.Defeat=copy.Defeat;});
            }));
        }
        void EditObject(Named obj)=>Find.WindowStack.Add(new ObjectWindow(obj,value=>Edit(_=>{
            if(obj is Placement)Map.Objects[Map.Objects.FindIndex(x=>x.Id==obj.Id)]=(Placement)value;
            else if(obj is Region)Map.Regions[Map.Regions.FindIndex(x=>x.Id==obj.Id)]=(Region)value;
            else if(obj is Rule)Map.Rules[Map.Rules.FindIndex(x=>x.Id==obj.Id)]=(Rule)value;
            else if(obj is Objective)Map.Objectives[Map.Objectives.FindIndex(x=>x.Id==obj.Id)]=(Objective)value;
            else if(obj is HudWidget)Map.Hud[Map.Hud.FindIndex(x=>x.Id==obj.Id)]=(HudWidget)value;
        }),Project,Map));
        void DrawMap(Rect r) {
            Vector2 localMouse=Event.current.mousePosition-r.position;
            Widgets.DrawBoxSolid(r,new Color(0.08f,0.10f,0.12f));
            GUI.BeginGroup(r);
            var size=new Rect(canvasOffset.x,canvasOffset.y,Map.Width*zoom,Map.Height*zoom);
            Widgets.DrawBoxSolid(size,new Color(0.27f,0.30f,0.22f));
            foreach(var p in Map.Objects){
                if(!showAux&&p.Kind=="Marker")continue;
                var at=new Rect(canvasOffset.x+p.Position.X*zoom,canvasOffset.y+(Map.Height-1-p.Position.Z)*zoom,zoom,zoom);
                if(p.Kind=="Thing") {
                    var d=DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name);
                    if(d!=null) {var bounds=GenAdj.OccupiedRect(new IntVec3(p.Position.X,0,p.Position.Z),new Rot4(p.Rotation),d.size);
                        at=new Rect(canvasOffset.x+bounds.minX*zoom,canvasOffset.y+(Map.Height-1-bounds.maxZ)*zoom,bounds.Width*zoom,bounds.Height*zoom);
                        if(IsSelected(p.Id))IconOutline(at,d);Widgets.DefIcon(at,d);}
                }else if(p.Kind=="Boss") {
                    Widgets.DrawBoxSolid(at,new Color(.8f,.15f,.12f));
                }else if(p.Kind=="PawnKind") {
                    var kind=DefDatabase<PawnKindDef>.GetNamedSilentFail(p.Def.Name);if(kind!=null){if(IsSelected(p.Id))IconOutline(at,kind.race);Widgets.DefIcon(at,kind.race);}
                }else Widgets.DrawBoxSolid(at,p.Kind=="Pawn"?Color.cyan:p.Kind=="Marker"?Color.yellow:p.Kind=="Roof"?new Color(.5f,.5f,.7f):TerrainColor(p.Def.Name));
                if(IsSelected(p.Id))SelectionBorder(at);
            }
            if(showAux)foreach(var rule in Map.Rules){
                var at=new Rect(canvasOffset.x+rule.Position.X*zoom,canvasOffset.y+(Map.Height-1-rule.Position.Z)*zoom,Math.Max(20,zoom),Math.Max(20,zoom));
                if(Widgets.ButtonText(at,"T")){selectedId=rule.Id;selection.Clear();if(Event.current.clickCount==2)EditObject(rule);}
                TooltipHandler.TipRegion(at,rule.Name+"\n宿主："+EditorUI.HostLabel(Map,rule.Host)+"\n"+(rule.Once?"一次性":"持久（可重复）"));
                if(IsSelected(rule.Id)) {
                    SelectionBorder(at);
                    var host=Map.Objects.FirstOrDefault(o=>o.Id==rule.Host||(rule.Host=="role:"+o.Role&&o.Kind=="Pawn"));
                    var center=host?.Position??rule.Position;
                    if(rule.Events.Any(e=>e.Kind=="RangeEnter"||e.Kind=="RangeLeave")) {
                        var origin=new Vector2(canvasOffset.x+(center.X+.5f)*zoom,canvasOffset.y+(Map.Height-center.Z-.5f)*zoom);
                        for(int segment=0;segment<64;segment++){
                            float a=segment*Mathf.PI/32,b=(segment+1)*Mathf.PI/32;
                            Widgets.DrawLine(origin+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*rule.Radius*zoom,origin+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*rule.Radius*zoom,new Color(.2f,.85f,1f,.7f),1);
                        }
                    }
                }
            }
            if(showAux)foreach(var region in Map.Regions){
                var box=new Rect(canvasOffset.x+region.Min.X*zoom,canvasOffset.y+(Map.Height-1-region.Max.Z)*zoom,(region.Max.X-region.Min.X+1)*zoom,(region.Max.Z-region.Min.Z+1)*zoom);
                Widgets.DrawBoxSolid(box,new Color(.2f,.5f,.9f,.22f));Widgets.Label(box,region.Name);if(IsSelected(region.Id))SelectionBorder(box);
            }
            // GUI group coordinates: require both the visible canvas and actual map bounds.
            var mouse=localMouse;
            bool selected=brush!=null||tool=="Pawn"||tool=="Marker"||tool=="Region"||tool=="Trigger"||Map.Objects.Any(p=>selection.Contains(p.Id)||p.Id==selectedId);
            if(zoom>=6&&selected&&new Rect(0,0,r.width,r.height).Contains(mouse)&&size.Contains(mouse)) {
                int cx=Mathf.FloorToInt((mouse.x-canvasOffset.x)/zoom),cy=Mathf.FloorToInt((mouse.y-canvasOffset.y)/zoom);
                int minX=Math.Max(0,cx-4),maxX=Math.Min(Map.Width,cx+5),minY=Math.Max(0,cy-4),maxY=Math.Min(Map.Height,cy+5);
                var tint=new Color(.8f,.88f,1f,.23f);
                for(int x=minX;x<=maxX;x++)Widgets.DrawLine(new Vector2(canvasOffset.x+x*zoom,canvasOffset.y+minY*zoom),new Vector2(canvasOffset.x+x*zoom,canvasOffset.y+maxY*zoom),tint,1);
                for(int y=minY;y<=maxY;y++)Widgets.DrawLine(new Vector2(canvasOffset.x+minX*zoom,canvasOffset.y+y*zoom),new Vector2(canvasOffset.x+maxX*zoom,canvasOffset.y+y*zoom),tint,1);
            }
            GUI.EndGroup();
            MapInput(r);
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
        static Color TerrainColor(string name)=>name.IndexOf("Water",StringComparison.OrdinalIgnoreCase)>=0?new Color(.15f,.35f,.65f):name.IndexOf("Rock",StringComparison.OrdinalIgnoreCase)>=0?Color.gray:new Color(.45f,.37f,.22f);
        Cell CellAt(Vector2 point,Rect r)=>new Cell{X=Mathf.FloorToInt((point.x-r.x-canvasOffset.x)/zoom),Z=Map.Height-1-Mathf.FloorToInt((point.y-r.y-canvasOffset.y)/zoom)};
        void MapInput(Rect r){
            var e=Event.current;
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
            if(e.type==EventType.ScrollWheel){zoom=Mathf.Clamp(zoom-e.delta.y,2,32);e.Use();return;}
            var cell=CellAt(e.mousePosition,r);
            if(e.type==EventType.MouseDown&&e.button==0&&cell.X>=0&&cell.Z>=0&&cell.X<Map.Width&&cell.Z<Map.Height){
                if(tool=="Place"&&brush!=null){BeginPaint(brush,cell);e.Use();return;}
                if(tool=="Pawn"||tool=="Marker"){
                    BeginPaint(new Placement{Name=tool,Kind=tool,Role=tool=="Pawn"?Map.Roles.FirstOrDefault()??"hero":"",Layer="Auxiliary"},cell);e.Use();return;
                }
                if(tool=="Trigger"){AddTrigger(cell);e.Use();return;}
                if(tool=="Region"){origin=cell;dragging=true;dragStart=e.mousePosition;e.Use();return;}
                var hit=Map.Objects.LastOrDefault(p=>HitPlacement(p,cell)&&(!lockAux||p.Layer!="Auxiliary"));
                if(hit!=null){
                    if(!e.shift&&!selection.Contains(hit.Id))selection.Clear();selection.Add(hit.Id);selectedId=hit.Id;
                    if(e.clickCount==2){EditObject(hit);e.Use();return;}
                    origin=cell;dragging=true;dragSnapshot=Json.Write(Project);originalPositions=Map.Objects.Where(p=>selection.Contains(p.Id)).ToDictionary(p=>p.Id,p=>Json.Copy(p.Position));
                }else{selection.Clear();selectedId="";}
                e.Use();
            }else if(e.type==EventType.MouseDrag&&e.button==0&&dragging&&tool!="Region"){
                foreach(var p in Map.Objects.Where(p=>originalPositions.ContainsKey(p.Id))){var o=originalPositions[p.Id];p.Position.X=Mathf.Clamp(o.X+cell.X-origin.X,0,Map.Width-1);p.Position.Z=Mathf.Clamp(o.Z+cell.Z-origin.Z,0,Map.Height-1);}e.Use();
            }else if(e.type==EventType.MouseUp&&e.button==0&&dragging){
                dragging=false;
                if(tool=="Region")Edit(_=>Map.Regions.Add(new Region{Name="区域",Min=new Cell{X=Math.Max(0,Math.Min(origin.X,cell.X)),Z=Math.Max(0,Math.Min(origin.Z,cell.Z))},Max=new Cell{X=Math.Min(Map.Width-1,Math.Max(origin.X,cell.X)),Z=Math.Min(Map.Height-1,Math.Max(origin.Z,cell.Z))}}));
                else {var positions=Map.Objects.Where(p=>originalPositions.ContainsKey(p.Id)).ToDictionary(p=>p.Id,p=>Json.Copy(p.Position));
                    foreach(var p in Map.Objects.Where(p=>originalPositions.ContainsKey(p.Id)))p.Position=originalPositions[p.Id];
                    Edit(_=>{foreach(var p in Map.Objects.Where(p=>positions.ContainsKey(p.Id)))p.Position=positions[p.Id];});
                }e.Use();
            }
        }
        void BeginPaint(Placement template,Cell cell) {
            painting=true;paintSnapshot=Json.Write(Project);paintBrush=Json.Copy(template);painted.Clear();paintLast=null;selection.Clear();PaintTo(cell);
        }
        void FinishPaint() {
            if(!painting)return;
            painting=false;history.CommitAppliedChange(paintSnapshot);paintSnapshot=null;paintBrush=null;paintLast=null;painted.Clear();
        }
        public override void PostClose(){FinishPaint();base.PostClose();}
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
            if(Map.Objects.Any(o=>o.Kind==p.Kind&&o.Position.X==cell.X&&o.Position.Z==cell.Z&&o.Def?.Name==p.Def?.Name&&o.Rotation==p.Rotation&&o.Faction==p.Faction&&o.Boss==p.Boss&&o.Role==p.Role&&o.Stuff?.Name==p.Stuff?.Name))return;
            var def=p.Kind=="Thing"?DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name):null;
            if(def!=null) {
                var bounds=GenAdj.OccupiedRect(new IntVec3(cell.X,0,cell.Z),new Rot4(p.Rotation),def.size);
                if(bounds.minX<0||bounds.minZ<0||bounds.maxX>=Map.Width||bounds.maxZ>=Map.Height)return;
                if(def.category==ThingCategory.Building)foreach(var other in Map.Objects.Where(o=>o.Kind=="Thing")) {
                    var otherDef=DefDatabase<ThingDef>.GetNamedSilentFail(other.Def.Name);if(otherDef?.category!=ThingCategory.Building)continue;
                    var occupied=GenAdj.OccupiedRect(new IntVec3(other.Position.X,0,other.Position.Z),new Rot4(other.Rotation),otherDef.size);
                    if(bounds.minX<=occupied.maxX&&bounds.maxX>=occupied.minX&&bounds.minZ<=occupied.maxZ&&bounds.maxZ>=occupied.minZ)return;
                }
            }
            var placed=Json.Copy(p);placed.Id=Guid.NewGuid().ToString("N");placed.Position=cell;
            if(p.Kind=="Terrain"||p.Kind=="Roof")Map.Objects.RemoveAll(o=>o.Kind==p.Kind&&o.Position.X==cell.X&&o.Position.Z==cell.Z);
            Map.Objects.Add(placed);selectedId=placed.Id;
        }
        void AddTrigger(Cell cell){
            var rule=new Rule{Name="触发器 "+(Map.Rules.Count+1)};
            // Trigger locations are editor layout metadata, not an additional runtime entity.
            rule.Position=cell;Edit(_=>Map.Rules.Add(rule));selectedId=rule.Id;EditObject(rule);
        }
        void Copy(){clipboard=Json.Copy(Map.Objects.Where(p=>selection.Contains(p.Id)).ToList());status="已复制 "+clipboard.Count+" 个对象";}
        void Paste(){Edit(_=>{var copy=History<Project>.CopyObjects(clipboard,1,1);Map.Objects.AddRange(copy);selection=new HashSet<string>(copy.Select(p=>p.Id));});}
        void Delete(){
            var ids=selection.Count>0?selection:new HashSet<string>{selectedId};
            var uses=Validation.Actions(Map.Rules.SelectMany(x=>x.Actions).Concat(Map.Entry).Concat(Map.Defeat)).Where(a=>ids.Contains(a.Target)||ids.Contains(a.Text)).Select(a=>EditorUI.Label(a)).ToList();
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("删除所选对象？引用将需要重新绑定。\n"+string.Join("\n",uses),()=>Edit(_=>{
                Map.Objects.RemoveAll(o=>ids.Contains(o.Id));Map.Rules.RemoveAll(o=>ids.Contains(o.Id));Map.Regions.RemoveAll(o=>ids.Contains(o.Id));Map.Objectives.RemoveAll(o=>ids.Contains(o.Id));Map.Hud.RemoveAll(o=>ids.Contains(o.Id));selection.Clear();selectedId="";
            })));
        }
        void HandleKeys(){
            var e=Event.current;if(e.type!=EventType.KeyDown)return;
            FinishPaint();
            if(e.control&&e.keyCode==KeyCode.S){Save();e.Use();}
            if(e.control&&e.keyCode==KeyCode.Z){history.Undo();e.Use();}
            if(e.control&&e.keyCode==KeyCode.Y){history.Redo();e.Use();}
            if(e.control&&e.keyCode==KeyCode.C){Copy();e.Use();}
            if(e.control&&e.keyCode==KeyCode.V){Paste();e.Use();}
            if(e.keyCode==KeyCode.Delete){Delete();e.Use();}
            if(e.keyCode==KeyCode.R){Edit(_=>{foreach(var p in Map.Objects.Where(p=>selection.Contains(p.Id)))p.Rotation=(p.Rotation+1)%4;});e.Use();}
        }
        void Resize(Rect r,Rect center){
            var e=Event.current;
            var l=new Rect(left,84,7,r.height-120);var rr=new Rect(r.width-right-7,84,7,r.height-120);
            if(e.type==EventType.MouseDown&&e.button==0){dragLeft=leftOpen&&l.Contains(e.mousePosition);dragRight=rightOpen&&rr.Contains(e.mousePosition);}
            if(e.type==EventType.MouseDrag){if(dragLeft){left=Mathf.Clamp(e.mousePosition.x,160,r.width*.35f);e.Use();}if(dragRight){right=Mathf.Clamp(r.width-e.mousePosition.x,190,r.width*.4f);e.Use();}}
            if(e.type==EventType.MouseUp){dragLeft=dragRight=false;}
        }
        void GenerateBase(){
            Prompt("生成种子", "12345",seed=>Edit(_=>{
                Map.Base="Generated";Map.Objects.RemoveAll(p=>p.Kind=="Terrain");
                int hash=17;foreach(char c in seed)hash=hash*31+c;var random=new System.Random(hash);
                var soil=DefDatabase<TerrainDef>.GetNamed("Soil");var gravel=DefDatabase<TerrainDef>.GetNamedSilentFail("Gravel")??soil;
                for(int z=0;z<Map.Height;z++)for(int x=0;x<Map.Width;x++)if(random.NextDouble()<.17)Map.Objects.Add(new Placement{Kind="Terrain",Name=gravel.label,Def=Profiles.Ref(gravel),Position=new Cell{X=x,Z=z},Layer="Terrain"});
            }));
        }
        string graphNode="";bool nodeDragging;Vector2 nodeStart,mouseStart;
        void DrawGraph(Rect r){
            if(Project.Campaigns.Count==0){if(Widgets.ButtonText(r,"创建战役"))Edit(p=>p.Campaigns.Add(new Campaign{Name=p.Name}));return;}
            var c=Project.Campaigns[0];Widgets.DrawBoxSolid(r,new Color(.09f,.11f,.13f));GUI.BeginGroup(r);
            if(Widgets.ButtonText(new Rect(0,0,100,30),"+ 关卡"))Edit(_=>{var n=new Node{Name="关卡 "+(c.Nodes.Count+1),Map=mapId,X=50+c.Nodes.Count*35,Y=80+c.Nodes.Count*35};c.Nodes.Add(n);if(c.Start=="")c.Start=n.Id;});
            if(Widgets.ButtonText(new Rect(105,0,100,30),"+ 连接"))Find.WindowStack.Add(new ObjectWindow(new Edge{From=graphNode},o=>Edit(_=>c.Edges.Add((Edge)o)),Project));
            if(Widgets.ButtonText(new Rect(210,0,120,30),"战役设置"))Find.WindowStack.Add(new ObjectWindow(c,o=>Edit(p=>p.Campaigns[0]=(Campaign)o),Project));
            foreach(var edge in c.Edges){
                var from=c.Nodes.FirstOrDefault(n=>n.Id==edge.From);var to=c.Nodes.FirstOrDefault(n=>n.Id==edge.To);if(from==null||to==null)continue;
                var a=new Vector2(from.X+160,from.Y+35)+graphOffset;var b=new Vector2(to.X,to.Y+35)+graphOffset;
                Widgets.DrawLine(a,b,Color.gray,2);
                if(Widgets.ButtonText(new Rect((a.x+b.x)/2-65,(a.y+b.y)/2-13,130,26),edge.Result+" / "+edge.When.Op))Find.WindowStack.Add(new ObjectWindow(edge,o=>Edit(_=>c.Edges[c.Edges.IndexOf(edge)]=(Edge)o),Project));
            }
            foreach(var n in c.Nodes){
                var box=new Rect(n.X+graphOffset.x,n.Y+graphOffset.y,160,72);Widgets.DrawWindowBackground(box);
                Widgets.Label(box.ContractedBy(5),(c.Start==n.Id?"▶ ":"")+n.Name+"\n"+n.Kind);
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
            Widgets.Label(new Rect(0,0,r.width-40,32),"地图尺寸（格）");
            Widgets.Label(new Rect(0,44,60,32),"宽 X");width=Widgets.TextField(new Rect(65,44,130,32),width);
            Widgets.Label(new Rect(235,44,60,32),"高 Z");height=Widgets.TextField(new Rect(300,44,130,32),height);
            Widgets.Label(new Rect(0,92,r.width,62),"范围：20–500。已有内容保持原坐标。\n缩小前请移走边界外的内容；修改支持撤销。");
            Widgets.Label(new Rect(0,158,r.width,85),error);
            if(Widgets.ButtonText(new Rect(0,r.height-38,r.width,36),"应用尺寸")) {
                if(!int.TryParse(width,out int w)||!int.TryParse(height,out int h))error="请输入整数。";
                else {error=apply(w,h)??"";if(error=="")Close();}
            }
        }
    }
    public class TextPrompt : Window
    {
        readonly string title;string value;readonly Action<string> done;public override Vector2 InitialSize=>new Vector2(760,180);
        public TextPrompt(string title,string value,Action<string> done){this.title=title;this.value=value;this.done=done;doCloseX=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect r){
            Widgets.Label(new Rect(0,0,r.width,30),title);value=Widgets.TextField(new Rect(0,40,r.width,32),value);
            if(Widgets.ButtonText(new Rect(0,90,r.width,35),"确认")){try{done(value);Close();}catch(Exception ex){EditorUI.Error(ex);}}
        }
    }
    public class ActionsWindow : ListWindow
    {
        readonly Action apply;
        public ActionsWindow(List<Act> actions,Project p,Action apply):base(actions,p){this.apply=apply;}
        public override void PostClose(){base.PostClose();apply();}
    }
}
