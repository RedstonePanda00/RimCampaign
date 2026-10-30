using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;
using Region=RimCampaign.Core.Region;

namespace RimCampaign.Editor {
    public partial class Workspace {
        string libraryTab="Assets",logicTab="Triggers",settingsTab="Basic",sceneFilter="All";
        Vector2 pageScroll;
        ObjectWindow inlineEditor;object inlineSource;int inlineRevision=-1;
        static readonly Color PanelColor=new Color(.115f,.135f,.16f),ChromeColor=new Color(.085f,.10f,.12f);
        void SwitchWorkspace(string value){CancelPointerGestures();tab=value;tool="";brush=null;EditorFocus.Release();}
        void UndoEdit(bool redo){CancelPointerGestures();if(redo)history.Redo();else history.Undo();inlineEditor=null;terrainPreviewDirty=true;}
        static bool ShellButton(Rect rect,string text,bool active=false){
            if(active){EditorUI.Rounded(rect,new Color(.34f,.37f,.41f));EditorUI.Rounded(rect.ContractedBy(1),new Color(.20f,.225f,.26f),3);}
            var result=EditorUI.Button(rect,text,false);
            return result;
        }
        void Menu(params (string Label,Action Run)[] entries)=>Find.WindowStack.Add(new FloatMenu(entries.Select(e=>new FloatMenuOption(e.Label,e.Run)).ToList()));
        void DrawEditorShell(Rect r){
            Widgets.DrawBoxSolid(new Rect(0,0,r.width,76),ChromeColor);
            float x=8;void M(string label,float width,Action run){if(ShellButton(new Rect(x,0,width,30),label))run();x+=width+5;}
            M(RimCampaign.Runtime.RcText.T("文件"),64,()=>Menu((RimCampaign.Runtime.RcText.T("新建项目"),()=>{history=new History<Project>(NewProject());mapId=Map.Id;path="";mapView.Reset();selectedId="";selection.Clear();inlineEditor=null;}),
                (RimCampaign.Runtime.RcText.T("打开项目…"),()=>Prompt(RimCampaign.Runtime.RcText.T("项目 JSON 路径"),path,OpenProject)),
                (RimCampaign.Runtime.RcText.T("保存"),Save),(RimCampaign.Runtime.RcText.T("另存为…"),()=>Prompt(RimCampaign.Runtime.RcText.T("另存为"),path,s=>{ProjectFiles.Save(s,Project);path=ProjectFiles.ResolvePath(s,false);})),(RimCampaign.Runtime.RcText.T("导出…"),Export)));
            M(RimCampaign.Runtime.RcText.T("编辑"),64,()=>Menu((RimCampaign.Runtime.RcText.T("撤销  Ctrl+Z"),()=>UndoEdit(false)),(RimCampaign.Runtime.RcText.T("重做  Ctrl+Y"),()=>UndoEdit(true)),(RimCampaign.Runtime.RcText.T("复制  Ctrl+C"),Copy),(RimCampaign.Runtime.RcText.T("粘贴  Ctrl+V"),Paste),(RimCampaign.Runtime.RcText.T("删除  Delete"),Delete)));
            M(RimCampaign.Runtime.RcText.T("视图"),64,()=>Menu((leftOpen?RimCampaign.Runtime.RcText.T("收起左侧面板"):RimCampaign.Runtime.RcText.T("展开左侧面板"),()=>leftOpen=!leftOpen),(rightOpen?RimCampaign.Runtime.RcText.T("收起属性面板"):RimCampaign.Runtime.RcText.T("展开属性面板"),()=>rightOpen=!rightOpen),(RimCampaign.Runtime.RcText.T("适应全图"),()=>mapView.Fit()),(RimCampaign.Runtime.RcText.T("居中"),()=>mapView.Center())));
            M(RimCampaign.Runtime.RcText.T("项目"),64,()=>Menu((RimCampaign.Runtime.RcText.T("项目设置"),()=>OpenFields(Project,new[]{"Name","PackageId","Author","ManualDependencies","ManualLoadAfter","ExtraReferences","CustomAssemblies"},o=>history.Replace((Project)o))),
                (RimCampaign.Runtime.RcText.T("角色档案"),()=>Find.WindowStack.Add(new ProfilesWindow(Project,p=>Edit(v=>v.Profiles=p)))),("调用预设…",StructureMenu),(RimCampaign.Runtime.RcText.T("导入组合…"),ImportPrefab),(RimCampaign.Runtime.RcText.T("校验与依赖"),()=>SwitchWorkspace("Problems")),(RimCampaign.Runtime.RcText.T("C# 扩展…"),()=>Prompt(RimCampaign.Runtime.RcText.T("C# 文件路径"),"",Compile))));
            Text.Font=GameFont.Tiny;EditorUI.DrawLabel(new Rect(x+8,5,Math.Max(40,r.width-x-40),24),"RimCampaign  /  "+RcText.T(Project.Name));Text.Font=GameFont.Small;
            x=8;void N(string label,string value){if(ShellButton(new Rect(x,37,86,31),label,tab==value))SwitchWorkspace(value);x+=90;}
            N(RimCampaign.Runtime.RcText.T("地图"),"Map");N(RimCampaign.Runtime.RcText.T("剧情"),"Logic");N(RimCampaign.Runtime.RcText.T("战役"),"Campaign");
            float testX=r.width-91,saveX=testX-65,settingsX=saveX-94,mapX=x+12;
            if(ShellButton(new Rect(mapX,37,Math.Max(100,settingsX-mapX-8),31),RcText.T(Map.Name)+" ▾"))ChooseMap();
            if(ShellButton(new Rect(settingsX,37,88,31),RimCampaign.Runtime.RcText.T("地图设置"),tab=="Settings"))SwitchWorkspace("Settings");
            if(ShellButton(new Rect(saveX,37,60,31),RimCampaign.Runtime.RcText.T("保存")))Save();
            var old=GUI.color;GUI.color=new Color(.62f,.83f,1f);if(EditorUI.Button(new Rect(testX,37,85,31),RimCampaign.Runtime.RcText.T("热测试")))HotTest();GUI.color=old;
            var body=new Rect(0,84,r.width,r.height-116);
            if(tab=="Map"){
                float lx=leftOpen?Math.Min(left,r.width*.29f):0,rx=rightOpen?Math.Min(right,r.width*.31f):0;
                var center=new Rect(lx+8,84,r.width-lx-rx-16,body.height);
                if(leftOpen)DrawLibrary(new Rect(0,84,lx,body.height));
                if(rightOpen)DrawInspector(new Rect(r.width-rx,84,rx,body.height));
                DrawMapTools(new Rect(center.x,center.y,center.width,32));
                DrawMap(new Rect(center.x,center.y+36,center.width,center.height-36));Resize(r,center);
            }else if(tab=="Campaign")DrawGraph(body);
            else if(tab=="Settings")DrawSettings(body);
            else if(tab=="Problems")DrawProblems(body);
            else DrawLogic(body);
            Widgets.DrawBoxSolid(new Rect(0,r.height-28,r.width,28),ChromeColor);
            Text.Font=GameFont.Tiny;EditorUI.DrawLabel(new Rect(8,r.height-26,r.width-16,25),string.IsNullOrEmpty(status)?RimCampaign.Runtime.RcText.T("地图 ")+Map.Width+" × "+Map.Height+RimCampaign.Runtime.RcText.T("  ·  空白处左键拖框  ·  Shift 追加  ·  右键平移  ·  Ctrl+S 保存"):status);Text.Font=GameFont.Small;
        }
        void ChooseMap(){
            var choices=Project.Maps.Select(m=>new FloatMenuOption(RcText.T(m.Name),()=>{CancelPointerGestures();mapId=m.Id;selectedId="";selection.Clear();inlineEditor=null;mapView.Reset();})).ToList();
            choices.Add(new FloatMenuOption(RimCampaign.Runtime.RcText.T("＋ 新建地图"),()=>Edit(p=>{var m=new MapSpec{Name=RimCampaign.Runtime.RcText.T("地图 ")+(p.Maps.Count+1)};p.Maps.Add(m);mapId=m.Id;selectedId="";selection.Clear();})));
            Find.WindowStack.Add(new FloatMenu(choices));
        }
        void DrawMapTools(Rect r){
            Widgets.DrawBoxSolid(r,PanelColor);float x=r.x;
            void B(string label,float w,Action a,bool on=false){if(ShellButton(new Rect(x,r.y,w,r.height),label,on))a();x+=w+3;}
            B(RimCampaign.Runtime.RcText.T("选择"),66,()=>{CancelPointerGestures();tool="";brush=null;},tool=="");
            B(RimCampaign.Runtime.RcText.T("区域"),66,()=>{tool="Region";brush=null;},tool=="Region");
            B(RimCampaign.Runtime.RcText.T("触发器"),76,()=>{SetEditMode("Edit");tool="Trigger";brush=null;},tool=="Trigger");
            B(RimCampaign.Runtime.RcText.T("路线 ▾"),76,PatrolMenu,tool=="PatrolPoint");
            if(editMode!="Edit")B(RimCampaign.Runtime.RcText.T("形状 ▾"),65,()=>EditorUI.Pick(SurfaceShapes.Select(k=>new KeyValuePair<string,string>(k.Key,k.Value)),v=>surfaceShape=v));
            if(ShellButton(new Rect(r.xMax-82,r.y,82,r.height),RimCampaign.Runtime.RcText.T("显示 ▾"))){libraryTab="Layers";leftOpen=true;}
        }
        static string AssetCategoryLabel(string key)=>new Dictionary<string,string>{{"Thing",RimCampaign.Runtime.RcText.T("建筑与物品")},{"Floor",RimCampaign.Runtime.RcText.T("地表 / 地板")},{"Terrain",RimCampaign.Runtime.RcText.T("地表 / 地形")},{"Roof",RimCampaign.Runtime.RcText.T("地表 / 屋顶")},{"PawnKind",RimCampaign.Runtime.RcText.T("角色 / 单位类型")},{"Pawn",RimCampaign.Runtime.RcText.T("角色 / 战役角色")},{"Boss",RimCampaign.Runtime.RcText.T("角色 / Boss")},{"Marker",RimCampaign.Runtime.RcText.T("区域与标记 / 标记")},{"Region",RimCampaign.Runtime.RcText.T("区域与标记 / 区域")},{"Trigger",RimCampaign.Runtime.RcText.T("区域与标记 / 触发器")}}.TryGetValue(key,out var label)?label:key;
        void DrawLibrary(Rect r){
            Widgets.DrawBoxSolid(r,PanelColor);GUI.BeginGroup(r);float w=(r.width-8)/3;
            foreach(var pair in new[]{(RimCampaign.Runtime.RcText.T("素材"),"Assets"),(RimCampaign.Runtime.RcText.T("场景"),"Scene"),(RimCampaign.Runtime.RcText.T("图层"),"Layers")}.Select((v,i)=>new{v,i}))if(ShellButton(new Rect(pair.i*(w+4),0,w,31),pair.v.Item1,libraryTab==pair.v.Item2))libraryTab=pair.v.Item2;
            var content=new Rect(5,40,r.width-10,r.height-45);
            if(libraryTab=="Assets")AssetLibrary(content);
            else if(libraryTab=="Scene"){
                GUI.BeginGroup(content);search=Widgets.TextField(new Rect(0,0,content.width,29),search);
                if(EditorUI.Button(new Rect(0,34,content.width,28),RimCampaign.Runtime.RcText.T("筛选：")+SceneFilterLabel(sceneFilter)))EditorUI.Pick(new[]{"All","Things","Pawns","Triggers","Patrol","Routes","Interactions"}.Select(v=>new KeyValuePair<string,string>(v,SceneFilterLabel(v))),v=>sceneFilter=v);
                DrawSceneList(new Rect(0,68,content.width,content.height-68));GUI.EndGroup();
            }else{
                GUI.BeginGroup(content);bool roofs=showRoofs;Widgets.CheckboxLabeled(new Rect(0,0,content.width,30),RimCampaign.Runtime.RcText.T("显示屋顶"),ref roofs);showRoofs=roofs;
                Widgets.CheckboxLabeled(new Rect(0,38,content.width,30),RimCampaign.Runtime.RcText.T("显示辅助标记与范围"),ref showAux);
                Widgets.CheckboxLabeled(new Rect(0,76,content.width,30),RimCampaign.Runtime.RcText.T("锁定辅助对象"),ref lockAux);
                EditorUI.DrawLabel(new Rect(0,120,content.width,110),RimCampaign.Runtime.RcText.T("辅助层包含巡逻路点、触发器与区域。锁定后仍可查看，不参与地图框选和拖动。"));GUI.EndGroup();
            }GUI.EndGroup();
        }
        static string SceneFilterLabel(string s)=>s=="Interactions"?RcText.T("可操作建筑与封路"):s=="Routes"?RcText.T("巡逻路线"):s=="Things"?RimCampaign.Runtime.RcText.T("建筑与物品"):s=="Pawns"?RimCampaign.Runtime.RcText.T("角色"):s=="Triggers"?RimCampaign.Runtime.RcText.T("空间触发器"):s=="Patrol"?RimCampaign.Runtime.RcText.T("巡逻路点"):RimCampaign.Runtime.RcText.T("全部对象");
        string sceneListKey;int sceneListRevision=-1;List<Named> sceneList;
        void DrawSceneList(Rect r){
            EnsureSceneIndex();
            var key=sceneFilter+"\n"+search+"\n"+editMode+"\n"+category+"\n"+lockAux+"\n"+showAux+"\n"+showRoofs+"\n"+LanguageDatabase.activeLanguage.folderName;
            if(sceneList==null||sceneListRevision!=sceneRevision||sceneListKey!=key){
            sceneListKey=key;sceneListRevision=sceneRevision;
            var items=SceneItems().Where(o=>!(o is Rule rule)||TriggerGeometry.Visible(rule)).Where(o=>!(o is Placement p)||Selectable(p));
            if(sceneFilter=="Triggers")items=items.OfType<Rule>();
            if(sceneFilter=="Things")items=items.OfType<Placement>().Where(p=>p.Kind=="Thing");
            if(sceneFilter=="Pawns")items=items.OfType<Placement>().Where(p=>new[]{"Pawn","PawnKind","Boss"}.Contains(p.Kind));
            if(sceneFilter=="Patrol")items=items.OfType<Placement>().Where(p=>p.Layer=="Patrol");
            if(sceneFilter=="Routes")items=items.OfType<PatrolRoute>();
            if(sceneFilter=="Interactions")items=items.OfType<SceneInteractionSpec>();
            sceneList=items.Where(o=>(o.Name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0||RcText.T(o.Name).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)||o.Id.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).ToList();
            }
            var list=sceneList;
            Widgets.BeginScrollView(r,ref sceneScroll,new Rect(0,0,r.width-18,list.Count*32));
            int start=Math.Max(0,(int)sceneScroll.y/32-1),end=Math.Min(list.Count,start+(int)(r.height/32)+3);
            for(int i=start;i<end;i++)if(ShellButton(new Rect(0,i*32,r.width-20,30),list[i].Name,IsSelected(list[i].Id))){if(Event.current.shift&&list[i] is Placement placed){SelectPlacement(placed,true);if(toggleOnRelease!=null){selection.Remove(toggleOnRelease);selectedId=selection.LastOrDefault()??"";toggleOnRelease=null;}}else FocusObject(list[i]);}
            Widgets.EndScrollView();
        }
        void DrawInspector(Rect r){
            Widgets.DrawBoxSolid(r,PanelColor);var obj=FindSceneItem(selectedId);
            GUI.BeginGroup(r.ContractedBy(8));r=new Rect(0,0,r.width-16,r.height-16);
            if(selection.Count>1){EditorUI.DrawLabel(new Rect(0,0,r.width,40),RimCampaign.Runtime.RcText.T("已选中 ")+selection.Count+RimCampaign.Runtime.RcText.T(" 个对象"));float y=45;
                if(EditorUI.Button(new Rect(0,y,r.width,30),RimCampaign.Runtime.RcText.T("批量修改阵营…")))PickSelectionFaction();y+=36;
                foreach(var item in new (string,Action)[]{("导出预设…",ExportStructure),(RimCampaign.Runtime.RcText.T("整组旋转  R"),()=>RotateSelection()),(RimCampaign.Runtime.RcText.T("原地转向  Shift+R"),()=>RotateSelection(true)),(RimCampaign.Runtime.RcText.T("复制  Ctrl+C"),Copy),(RimCampaign.Runtime.RcText.T("删除…"),Delete),(RimCampaign.Runtime.RcText.T("保存为组合…"),SavePrefab),(RimCampaign.Runtime.RcText.T("绑定巡逻路线…"),()=>EditorUI.Pick(AllPatrolRoutes.Select(a=>new KeyValuePair<string,string>(a.Id,a.Name)),BindPatrol))}){if(EditorUI.Button(new Rect(0,y,r.width,30),item.Item1))item.Item2();y+=36;}
                GUI.EndGroup();return;
            }
            if(obj==null){EditorUI.DrawLabel(new Rect(0,0,r.width,70),brush!=null?RimCampaign.Runtime.RcText.T("当前笔刷：")+brush.Name:RimCampaign.Runtime.RcText.T("选择地图对象以编辑属性。"));if(brush?.Kind=="Thing"&&EditorUI.Button(new Rect(0,78,r.width,32),RimCampaign.Runtime.RcText.T("笔刷款式…")))Find.WindowStack.Add(new GraphicVariantWindow(brush,true,i=>brush.GraphicIndex=i));GUI.EndGroup();return;}
            EditorUI.DrawLabel(new Rect(0,0,r.width,32),obj.Name);
            var structureAction=SelectedStructureAction();
            if(structureAction!=null){if(EditorUI.Button(new Rect(0,35,r.width,32),"结构入场设置…"))EditStructureArrival(structureAction);if(EditorUI.Button(new Rect(0,74,r.width,32),"中心位置与朝向…"))OpenFields(obj,new[]{"Name","Position","Rotation"},v=>ApplyObject(obj,v));GUI.EndGroup();return;}
            if(ShellButton(new Rect(0,35,r.width/2-3,28),RimCampaign.Runtime.RcText.T("复制"),false))Copy();
            if(ShellButton(new Rect(r.width/2+3,35,r.width/2-3,28),RimCampaign.Runtime.RcText.T("删除…"),false))Delete();
            if(inlineEditor==null||inlineSource!=obj||inlineRevision!=sceneRevision){inlineSource=obj;inlineRevision=sceneRevision;inlineEditor=new ObjectWindow(obj,value=>ApplyObject(obj,value),Project,Map);}
            if(ShellButton(new Rect(0,70,r.width/2-3,28),RcText.T("概览与关联"),inspectorTab=="Overview"))inspectorTab="Overview";
            if(ShellButton(new Rect(r.width/2+3,70,r.width/2-3,28),RcText.T("属性与备注"),inspectorTab=="Properties"))inspectorTab="Properties";
            if(inspectorTab=="Overview")DrawOverview(obj,new Rect(0,105,r.width,r.height-105));
            else inlineEditor.DrawEmbedded(new Rect(0,105,r.width,r.height-105));inlineSource=FindSceneItem(selectedId);inlineRevision=sceneRevision;GUI.EndGroup();
        }
        // Typed serialization avoids losing derived fields through Named.
        public class InspectorValue {public Rule Rule{get;set;}public Placement Placement{get;set;}public Objective Objective{get;set;}public HudWidget Hud{get;set;}public Region Region{get;set;}}
        void ApplyObject(Named obj,object value){Edit(_=>{
            if(obj is Placement){PlacementStorage.Validate((Placement)value);Map.Objects[Map.Objects.FindIndex(x=>x.Id==obj.Id)]=(Placement)value;}
            else if(obj is Rule)Map.Rules[Map.Rules.FindIndex(x=>x.Id==obj.Id)]=(Rule)value;
            else if(obj is Region)Map.Regions[Map.Regions.FindIndex(x=>x.Id==obj.Id)]=(Region)value;
            else if(obj is Objective)Map.Objectives[Map.Objectives.FindIndex(x=>x.Id==obj.Id)]=(Objective)value;
            else if(obj is HudWidget)Map.Hud[Map.Hud.FindIndex(x=>x.Id==obj.Id)]=(HudWidget)value;
            else if(obj is SceneInteractionSpec)Map.Interactions[Map.Interactions.FindIndex(x=>x.Id==obj.Id)]=(SceneInteractionSpec)value;
            else if(obj is ActionGroup)Map.Groups[Map.Groups.FindIndex(x=>x.Id==obj.Id)]=(ActionGroup)value;
            else if(obj is MapRewardSpec)Map.Rewards[Map.Rewards.FindIndex(x=>x.Id==obj.Id)]=(MapRewardSpec)value;
            else if(obj is PatrolRoute route){var routes=Map.PatrolRoutes.Any(x=>x.Id==obj.Id)?Map.PatrolRoutes:Map.Stealth.Routes;var updated=(PatrolRoute)value;routes[routes.FindIndex(x=>x.Id==obj.Id)]=updated;foreach(var point in updated.Points){var marker=Map.Objects.FirstOrDefault(x=>x.Id==point.Marker);if(marker!=null){marker.Position=Json.Copy(point.Position);marker.Rotation=point.Facing;}}}
        });status=RimCampaign.Runtime.RcText.T("已应用属性；可撤销。");}
        void ImportPrefab()=>Prompt(RimCampaign.Runtime.RcText.T("组合 JSON 路径"),"",s=>{ruleClipboard.Clear();clipboard=Json.Load<List<Placement>>(ProjectFiles.ResolvePath(s,true));Paste();});
        void SavePrefab(){if(SelectedPlacements().Count==0){status=RimCampaign.Runtime.RcText.T("组合库只保存实体布置；逻辑触发器请使用复制。");return;}Prompt(RimCampaign.Runtime.RcText.T("组合文件"),Path.Combine(WorkRoot,"Prefabs",RimCampaign.Runtime.RcText.T("组合.json")),s=>Json.Save(s,SelectedPlacements()));}
        void OpenFields(object obj,string[] fields,Action<object> commit)=>Find.WindowStack.Add(new ObjectWindow(obj,commit,Project,Map){VisibleFields=new HashSet<string>(fields)});
        void DrawLogic(Rect r){
            float nav=170,prop=rightOpen?Math.Min(right,r.width*.32f):0;Widgets.DrawBoxSolid(new Rect(r.x,r.y,nav,r.height),PanelColor);
            float y=r.y+8;foreach(var item in new[]{(RimCampaign.Runtime.RcText.T("触发器"),"Triggers"),("任务树 / Task trees","Tasks"),(RimCampaign.Runtime.RcText.T("任务目标"),"Objectives"),(RimCampaign.Runtime.RcText.T("演出与通讯"),"Scenes"),(RimCampaign.Runtime.RcText.T("游戏内 HUD"),"Hud")}){if(ShellButton(new Rect(r.x+6,y,nav-12,32),item.Item1,logicTab==item.Item2)){logicTab=item.Item2;pageScroll=Vector2.zero;}y+=38;}
            var area=new Rect(r.x+nav+10,r.y,r.width-nav-prop-20,r.height);GUI.BeginGroup(area);
            if(logicTab=="Tasks"){
                EditorUI.DrawLabel(new Rect(0,0,area.width,70),"单位绑定 → 等待条件 → 路点 → 操作 → 下一步\nActor → condition → waypoint → operation → next step");
                if(EditorUI.Button(new Rect(0,80,area.width,36),"编辑任务树 / Edit task trees")){var copy=Json.Copy(Map.Groups);Find.WindowStack.Add(new TaskTreesWindow(copy,Project,Map,()=>Edit(_=>Map.Groups=copy)));}
            }else if(logicTab=="Scenes"){
                if(EditorUI.Button(new Rect(0,0,area.width,34),RimCampaign.Runtime.RcText.T("入场演出与通讯")))EditActions("Entry");
                if(EditorUI.Button(new Rect(0,44,area.width,34),RimCampaign.Runtime.RcText.T("失败演出")))EditActions("Defeat");
                if(EditorUI.Button(new Rect(0,88,area.width,34),RimCampaign.Runtime.RcText.T("预览入场／失败演出")))Find.WindowStack.Add(new PreviewWindow(Map));
                if(EditorUI.Button(new Rect(0,132,area.width,34),"公共动作组 → 任务树 / Shared groups → Task trees")){logicTab="Tasks";pageScroll=Vector2.zero;}
                EditorUI.DrawLabel(new Rect(0,182,area.width,90),RimCampaign.Runtime.RcText.T("触发器内的通讯与演出在其动作列表编辑；此处管理地图入场、失败和公共动作组。"));
            }else{
                if(EditorUI.Button(new Rect(0,0,area.width,32),logicTab=="Triggers"?RimCampaign.Runtime.RcText.T("＋ 添加非空间触发器"):RimCampaign.Runtime.RcText.T("＋ 添加"))){
                    Named item=logicTab=="Triggers"?(Named)new Rule{Name=RimCampaign.Runtime.RcText.T("逻辑触发器")}:logicTab=="Objectives"?new Objective{Name=RimCampaign.Runtime.RcText.T("新目标")}:(Named)new HudWidget{Name=RimCampaign.Runtime.RcText.T("信息")};
                    Edit(_=>{if(item is Rule rule)Map.Rules.Add(rule);else if(item is Objective objective)Map.Objectives.Add(objective);else Map.Hud.Add((HudWidget)item);});InspectSelection(item.Id);
                }
                search=Widgets.TextField(new Rect(0,40,area.width,28),search);
                var items=(logicTab=="Triggers"?Map.Rules.Cast<Named>():logicTab=="Objectives"?Map.Objectives.Cast<Named>():Map.Hud.Cast<Named>()).Where(o=>(o.Name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0||RcText.T(o.Name).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)||o.Id.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).ToList();
                Widgets.BeginScrollView(new Rect(0,78,area.width,area.height-78),ref pageScroll,new Rect(0,0,area.width-20,items.Count*62));
                for(int i=0;i<items.Count;i++){var item=items[i];float yy=i*62;
                    bool spatial=item is Rule rule&&TriggerGeometry.Visible(rule);
                    if(ShellButton(new Rect(0,yy,area.width-100,30),item.Name,IsSelected(item.Id)))InspectSelection(item.Id);
                    if(spatial&&EditorUI.Button(new Rect(area.width-96,yy,72,28),RimCampaign.Runtime.RcText.T("定位")))FocusObject(item);
                    Text.Font=GameFont.Tiny;EditorUI.DrawLabel(new Rect(5,yy+30,area.width-25,25),item is Rule rr?(spatial?RimCampaign.Runtime.RcText.T("空间 · "):RimCampaign.Runtime.RcText.T("逻辑 · "))+string.Join(" / ",rr.Events.Select(e=>EditorUI.EventLabel(e.Kind))):item.Id);Text.Font=GameFont.Small;
                }Widgets.EndScrollView();
            }GUI.EndGroup();if(rightOpen)DrawInspector(new Rect(r.xMax-prop,r.y,prop,r.height));
        }
        void DrawSettings(Rect r){
            float nav=165;Widgets.DrawBoxSolid(new Rect(r.x,r.y,nav,r.height),PanelColor);float y=r.y+8;
            foreach(var item in new[]{(RimCampaign.Runtime.RcText.T("基础"),"Basic"),(RimCampaign.Runtime.RcText.T("生成与奖励"),"Generation"),(RimCampaign.Runtime.RcText.T("阵营"),"Factions"),(RimCampaign.Runtime.RcText.T("组件"),"Modules")}){if(ShellButton(new Rect(r.x+6,y,nav-12,32),item.Item1,settingsTab==item.Item2))settingsTab=item.Item2;y+=38;}
            var area=new Rect(r.x+nav+18,r.y+8,r.width-nav-28,r.height-16);GUI.BeginGroup(area);float yy=0;
            void B(string label,Action run){if(EditorUI.Button(new Rect(0,yy,Math.Min(540,area.width),34),label))run();yy+=44;}
            if(settingsTab=="Basic"){
                B(RimCampaign.Runtime.RcText.T("地图基本参数"),()=>OpenFields(Map,new[]{"Name","Base","Terrain","Roles","Inputs","Outputs"},o=>Edit(p=>p.Maps[p.Maps.FindIndex(m=>m.Id==mapId)]=(MapSpec)o)));
                B(RimCampaign.Runtime.RcText.T("地图尺寸  ")+Map.Width+" × "+Map.Height,()=>Find.WindowStack.Add(new MapSizeWindow(Map.Width,Map.Height,TryResizeMap)));
            }else if(settingsTab=="Generation"){
                B(RimCampaign.Runtime.RcText.T("地形与生态初始化…"),GenerateBase);
                B("袖珍地图初始化",InitializePocketMission);
                if(Map.Base=="PocketMission")B("导出任务地图…",ExportMissionMap);
                B(RimCampaign.Runtime.RcText.T("地图生成奖励（")+Map.Rewards.Count+"）",()=>{var copy=Json.Copy(Map.Rewards);Find.WindowStack.Add(new ListWindow(copy,Project,Map,()=>Edit(_=>Map.Rewards=copy)));});
                EditorUI.DrawLabel(new Rect(0,yy,area.width,80),RimCampaign.Runtime.RcText.T("容器内容物在地图选中容器后编辑。奖励仅在生成阶段创建，领取由剧情动作转移已有实例。"));
            }else if(settingsTab=="Factions")B(RimCampaign.Runtime.RcText.T("编辑本图阵营与关系"),()=>Find.WindowStack.Add(new MapFactionsWindow(Map,d=>Edit(p=>p.Maps[p.Maps.FindIndex(m=>m.Id==d.Id)]=d))));
            else{
                B(RimCampaign.Runtime.RcText.T("添加地图组件…"),()=>EditorUI.Pick(CampaignMapModules.Available.Where(t=>Map.Components.All(s=>s.Type!=t.FullName)).Select(t=>new KeyValuePair<string,string>(t.FullName,ModuleLabel(t.FullName))),type=>Edit(_=>Map.Components.Add(new MapComponentSpec{Type=type,Name=ModuleLabel(type)}))));
                Widgets.BeginScrollView(new Rect(0,yy,area.width,area.height-yy),ref pageScroll,new Rect(0,0,area.width-20,Map.Components.Count*112+60));
                for(int i=0;i<Map.Components.Count;i++){
                    var spec=Map.Components[i];float row=i*112;EditorUI.DrawLabel(new Rect(0,row,area.width-25,27),spec.Name);
                    float bw=Math.Min(110,(area.width-32)/3);
                    if(EditorUI.Button(new Rect(0,row+34,bw,30),spec.Enabled?RimCampaign.Runtime.RcText.T("启用 ✓"):RimCampaign.Runtime.RcText.T("已停用")))Edit(_=>spec.Enabled=!spec.Enabled);
                    if(EditorUI.Button(new Rect(bw+6,row+34,bw,30),RimCampaign.Runtime.RcText.T("设置…"))){
                        if(spec.Type==typeof(StealthMapComponent).FullName)OpenFields(Map.Stealth,typeof(StealthSpec).GetProperties().Select(p=>p.Name).Where(n=>n!="Enabled"&&n!="Routes").ToArray(),v=>Edit(_=>Map.Stealth=(StealthSpec)v));
                        else if(spec.Type==typeof(CombatPatrolMapComponent).FullName){var list=Json.Copy(Map.CombatPatrols);Find.WindowStack.Add(new ListWindow(list,Project,Map,()=>Edit(_=>Map.CombatPatrols=list)));}
                        else Find.WindowStack.Add(new ObjectWindow(spec,v=>Edit(_=>Map.Components[Map.Components.FindIndex(c=>c.Id==spec.Id)]=(MapComponentSpec)v),Project,Map));
                    }
                    if(EditorUI.Button(new Rect((bw+6)*2,row+34,bw,30),RimCampaign.Runtime.RcText.T("移除"))){Edit(_=>Map.Components.Remove(spec));break;}
                    Text.Font=GameFont.Tiny;EditorUI.DrawLabel(new Rect(0,row+70,area.width-25,35),spec.Type);Text.Font=GameFont.Small;
                }
                if(Map.Components.Count==0)EditorUI.DrawLabel(new Rect(0,0,area.width-25,60),RimCampaign.Runtime.RcText.T("本图未挂载任何组件。按需添加，潜行不会自动绑定。"));Widgets.EndScrollView();
            }GUI.EndGroup();
        }
        static string ModuleLabel(string type)=>type==typeof(StealthMapComponent).FullName?RimCampaign.Runtime.RcText.T("潜行"):type==typeof(CombatPatrolMapComponent).FullName?RimCampaign.Runtime.RcText.T("常规战斗巡逻"):type.Split('.').Last();
        List<Issue> problemIssues;List<string> dependencyLines;
        void RefreshProblems(){problemIssues=Validation.Check(Project);problemIssues.AddRange(PlacementIssues());var deps=Dependencies.Scan(Project,CampaignDef.Catalog());problemIssues.AddRange(deps.Errors.Select(e=>new Issue{Path="Dependencies",Message=e}));dependencyLines=deps.Uses.Select(u=>u.Key+" ← "+string.Join(", ",u.Value)).Concat(deps.LoadAfter.Select(v=>"loadAfter: "+v)).ToList();}
        void DrawProblems(Rect r){
            if(problemIssues==null)RefreshProblems();GUI.BeginGroup(r);if(EditorUI.Button(new Rect(0,0,180,32),RimCampaign.Runtime.RcText.T("重新校验")))RefreshProblems();EditorUI.DrawLabel(new Rect(195,0,r.width-200,32),problemIssues.Count==0?RimCampaign.Runtime.RcText.T("校验通过"):RimCampaign.Runtime.RcText.T("发现 ")+problemIssues.Count+RimCampaign.Runtime.RcText.T(" 个问题"));
            var rows=problemIssues.Select(i=>i.ToString()).Concat(new[]{RimCampaign.Runtime.RcText.T("依赖报告")}).Concat(dependencyLines).ToList();Widgets.BeginScrollView(new Rect(0,42,r.width,r.height-42),ref pageScroll,new Rect(0,0,r.width-20,rows.Count*42));
            for(int i=0;i<rows.Count;i++){var row=new Rect(0,i*42,r.width-25,38);if(i<problemIssues.Count){if(ShellButton(row,rows[i])){var id=problemIssues[i].Path.Split('/');var map=Project.Maps.FirstOrDefault(m=>m.Id==id[0]||m.Objects.Any(o=>o.Id==id[0]));if(map!=null){mapId=map.Id;var obj=SceneItems().FirstOrDefault(o=>o.Id==id.Last());if(obj!=null)FocusObject(obj);else SwitchWorkspace("Settings");}}}else EditorUI.DrawLabel(row,rows[i]);}Widgets.EndScrollView();GUI.EndGroup();
        }
    }
}
