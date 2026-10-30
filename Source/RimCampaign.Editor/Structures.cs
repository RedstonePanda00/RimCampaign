using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor {
    public partial class Workspace {
        internal StructureSpec CaptureStructure(bool includeSurface) {
            var selected=SelectedPlacements().Where(p=>p.Kind!="Marker"||p.Layer!="Patrol").ToList();
            if(selected.Count==0)throw new InvalidOperationException("请先选择建筑、物品或单位类型。");
            var unsupported=SelectedEditable().Where(p=>!(p.Kind=="Marker"&&p.Layer=="Patrol")&&!new[]{"Thing","PawnKind","Terrain","Roof"}.Contains(p.Kind)).ToList();
            if(unsupported.Count>0)throw new InvalidOperationException("预设不保存剧情角色、Boss、触发器或标记。请取消选择："+string.Join("、",unsupported.Select(p=>p.Name)));
            var patrols=Json.Copy(Map.CombatPatrols.Where(b=>selected.Any(p=>p.Id==b.Pawn)).ToList());
            var routes=Json.Copy(Map.PatrolRoutes.Where(r=>patrols.Any(b=>b.Route==r.Id)).ToList());
            var bounds=selected.Select(PlacementBounds).Concat(routes.SelectMany(r=>r.Points).Select(p=>new CellRect(p.Position.X,p.Position.Z,1,1))).ToList();
            int x=bounds.Min(b=>b.minX),z=bounds.Min(b=>b.minZ),maxX=bounds.Max(b=>b.maxX),maxZ=bounds.Max(b=>b.maxZ);
            if(includeSurface)selected=selected.Concat(Map.Objects.Where(p=>(p.Kind=="Terrain"||p.Kind=="Roof")&&p.Position.X>=x&&p.Position.X<=maxX&&p.Position.Z>=z&&p.Position.Z<=maxZ)).Distinct().ToList();
            var result=new StructureSpec{Name="预设",Width=maxX-x+1,Height=maxZ-z+1,Objects=Json.Copy(selected),Factions=Json.Copy(Map.Factions),Patrols=patrols,PatrolRoutes=routes};
            foreach(var p in result.Objects){p.Position.X-=x;p.Position.Z-=z;p.RelatedObjects.Clear();p.Group="";p.SpawnAtStart=true;}
            foreach(var point in routes.SelectMany(r=>r.Points)){point.Position.X-=x;point.Position.Z-=z;point.Marker="";}
            Structures.Validate(result);return result;
        }
        void ExportStructure()=>Menu(("仅导出所选对象（可空投）",()=>ExportStructure(false)),("包含占地区域的地表与屋顶",()=>ExportStructure(true)));
        void ExportStructure(bool surfaces) {
            var structure=CaptureStructure(surfaces);
            Prompt("预设名称",structure.Name,name=>{
                structure.Name=name;
                Prompt("预设文件",Path.Combine(WorkRoot,"Structures",Structures.DefName(structure)+".rcstructure.json"),file=>{
                    file=Path.GetFullPath(file);Structures.Export(file,structure);
                    var companion=Path.ChangeExtension(file,"xml");
                    new XDocument(new XElement("Defs",Structures.Definition(structure,"Structures/"+Path.GetFileName(file)))).Save(companion);
                    Edit(p=>{p.Structures??=new List<StructureSpec>();p.Structures.Add(structure);});
                    status="已导出独立预设。JSON 放入模组 Structures，配套 XML 放入 Defs；也可直接导入引用。";
                });
            });
        }
        void StructureMenu() {
            var choices=new List<FloatMenuOption>{new FloatMenuOption("导入预设文件…",()=>Prompt("预设 JSON 路径","",file=>{
                var value=Json.Load<StructureSpec>(ProjectFiles.ResolvePath(file,true));Structures.Validate(value);
                var existing=Project.Structures?.FirstOrDefault(s=>s.Id==value.Id);
                if(existing!=null&&Json.Write(existing)!=Json.Write(value))throw new InvalidOperationException("项目中已有不同内容的同名预设 ID；请先为新版本更换 ID。");
                if(existing==null)Edit(p=>{p.Structures??=new List<StructureSpec>();p.Structures.Add(value);});
                ChooseStructureArrival(existing??value);
            }))};
            foreach(var structure in Project.Structures??new List<StructureSpec>()){var s=structure;choices.Add(new FloatMenuOption(s.Name,()=>ChooseStructureArrival(s)));}
            foreach(var def in DefDatabase<CampaignStructureDef>.AllDefsListForReading){var d=def;choices.Add(new FloatMenuOption(d.LabelCap+" [Def]",()=>ChooseStructureArrival(d.Layout,d.defName)));}
            Find.WindowStack.Add(new FloatMenu(choices));
        }
        void ChooseStructureArrival(StructureSpec s,string defName=null)=>Menu(("放入地图（可继续编辑）",()=>PastePreset(s)),("初始存在",()=>AddStructureReference(s,"Ground",defName)),("空投舱抵达",()=>AddStructureReference(s,"Pods",defName)));
        internal void PastePreset(StructureSpec s){
            var center=new IntVec3(Map.Width/2,0,Map.Height/2);
            if(s.Width>Map.Width||s.Height>Map.Height)throw new InvalidOperationException("预设大于地图。");
            var copies=StructureSpawner.At(s,center,Rot4.North);var ids=new Dictionary<string,string>();
            foreach(var p in copies){ids[p.Id]=p.Id=Guid.NewGuid().ToString("N");}
            var factions=new List<MapFaction>();var factionIds=new Dictionary<string,string>();
            foreach(var f in s.Factions){var existing=Map.Factions.FirstOrDefault(v=>v.Id==f.Id);if(existing!=null&&(f.Id=="Player"||f.Id=="Hostile"||existing.Name==f.Name&&existing.Side==f.Side)){factionIds[f.Id]=existing.Id;continue;}
                var copy=Json.Copy(f);copy.Id=Guid.NewGuid().ToString("N");factions.Add(copy);factionIds[f.Id]=copy.Id;}
            foreach(var p in copies)if(factionIds.TryGetValue(p.Faction,out var faction))p.Faction=faction;
            var root=PrefabUtility.GetRoot(center,new IntVec2(s.Width,s.Height),Rot4.North);
            var routes=Json.Copy(s.PatrolRoutes??new List<PatrolRoute>());var routeIds=new Dictionary<string,string>();
            foreach(var r in routes){routeIds[r.Id]=r.Id=Guid.NewGuid().ToString("N");foreach(var point in r.Points){point.Position.X+=root.x;point.Position.Z+=root.z;
                var marker=new Placement{Kind="Marker",Layer="Patrol",Name=r.Name,Position=Json.Copy(point.Position),Rotation=point.Facing};point.Marker=marker.Id;copies.Add(marker);}}
            var patrols=Json.Copy(s.Patrols??new List<CombatPatrolSpec>());
            foreach(var p in patrols){p.Pawn=ids[p.Pawn];p.Route=routeIds[p.Route];}
            Edit(_=>{Map.Factions.AddRange(factions);Map.Objects.AddRange(copies);Map.PatrolRoutes.AddRange(routes);Map.CombatPatrols.AddRange(patrols);
                if(patrols.Count>0&&!Map.Components.Any(c=>c.Type==typeof(CombatPatrolMapComponent).FullName))Map.Components.Add(new MapComponentSpec{Type=typeof(CombatPatrolMapComponent).FullName,Name="战斗巡逻"});
                selection=new HashSet<string>(copies.Select(p=>p.Id));selectedId=copies[0].Id;
            });
            status="预设已展开为可编辑对象，巡逻路线与阵营绑定已保留；可撤销。";
        }
        internal void AddStructureReference(StructureSpec s,string arrival,string defName=null) {
            if(s.Width>Map.Width||s.Height>Map.Height)throw new InvalidOperationException("预设大于当前地图。");
            if(arrival=="Pods"&&s.Objects.Any(p=>p.Kind=="Terrain"||p.Kind=="Roof"))throw new InvalidOperationException("地表和屋顶不能空投，请使用仅包含物品、建筑及单位的预设。");
            var marker=new Placement{Name=s.Name+" · 预设中心",Kind="Marker",Layer="Auxiliary",Position=new Cell{X=Map.Width/2,Z=Map.Height/2}};
            var action=new Act{Name=s.Name,Kind="rc.spawnStructure",Target=defName??s.Id,Origin=marker.Id,Arrival=arrival};
            Edit(p=>{
                if(defName==null){p.Structures??=new List<StructureSpec>();if(!p.Structures.Any(v=>v.Id==s.Id))p.Structures.Add(Json.Copy(s));}
                else {var d=DefDatabase<CampaignStructureDef>.GetNamed(defName);if(!p.ExtraReferences.Any(r=>r.Kind=="CampaignStructureDef"&&r.Name==defName))p.ExtraReferences.Add(new DefRef{Kind="CampaignStructureDef",Name=defName,Package=d.modContentPack.PackageIdPlayerFacing});}
                Map.Objects.Add(marker);Map.Entry.Add(action);
            });
            InspectSelection(marker.Id);status="已添加预设引用；拖动中心标记可定位，R 旋转。右侧可修改入场方式；剧情编辑器可调整触发时机。";
        }
        Act SelectedStructureAction()=>Map.Entry.FirstOrDefault(a=>a.Kind=="rc.spawnStructure"&&a.Origin==selectedId);
        void EditStructureArrival(Act action)=>Find.WindowStack.Add(new ObjectWindow(action,value=>Edit(_=>Map.Entry[Map.Entry.FindIndex(a=>a.Id==action.Id)]=(Act)value),Project,Map));
        int structurePreviewRevision=-1;string structurePreviewMap;
        readonly List<Placement> structurePreview=new List<Placement>();
        void DrawStructureReferences() {
            if(structurePreviewRevision!=sceneRevision||structurePreviewMap!=Map.Id){
            structurePreviewRevision=sceneRevision;structurePreviewMap=Map.Id;structurePreview.Clear();
            foreach(var action in Validation.Actions(Map.Entry.Concat(Map.Rules.SelectMany(r=>r.Actions))).Where(a=>a.Kind=="rc.spawnStructure")) {
                var marker=Map.Objects.FirstOrDefault(p=>p.Id==action.Origin&&p.Kind=="Marker");if(marker==null)continue;
                var s=Project.Structures?.FirstOrDefault(v=>v.Id==action.Target||Structures.DefName(v)==action.Target)??DefDatabase<CampaignStructureDef>.GetNamedSilentFail(action.Target)?.Layout;
                if(s==null)continue;
                structurePreview.AddRange(StructureSpawner.At(s,new IntVec3(marker.Position.X,0,marker.Position.Z),new Rot4(marker.Rotation)));
            }
            }
            foreach(var p in structurePreview) {
                    var bounds=StructureSpawner.Bounds(p);
                    var rect=new Rect(canvasOffset.x+bounds.minX*zoom,canvasOffset.y+(Map.Height-1-bounds.maxZ)*zoom,bounds.Width*zoom,bounds.Height*zoom);
                    if(p.Kind=="Thing")DrawThingGraphic(p,DefDatabase<ThingDef>.GetNamed(p.Def.Name),rect,.7f);
                    else if(p.Kind=="PawnKind")DrawActorMarker(p,rect);
                    else Widgets.DrawBoxSolid(rect,p.Kind=="Roof"?new Color(.5f,.65f,.9f,.25f):TerrainColor(p.Def.Name));
            }
        }
    }
}
