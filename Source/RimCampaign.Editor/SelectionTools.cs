using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public partial class Workspace
    {
        bool boxSelecting,selectionMoved;Cell boxStart,boxEnd;string toggleOnRelease;
        string rotationSignature;List<Placement> rotationOriginals;int rotationSteps;bool rotationIndividual;
        static string TransformSignature(IEnumerable<Placement> values)=>string.Join("|",values.OrderBy(p=>p.Id).Select(p=>p.Id+":"+p.Position.X+","+p.Position.Z+","+p.Rotation));
        List<Placement> SelectedPlacements()=>Map.Objects.Where(p=>selection.Contains(p.Id)||(selection.Count==0&&p.Id==selectedId)).ToList();
        bool Selectable(Placement p){
            if(editMode=="Floor")return p.Kind=="Terrain"&&TerrainSurfaces.IsFloor(p);
            if(editMode=="Terrain")return p.Kind=="Terrain"&&!TerrainSurfaces.IsFloor(p);
            if(lockAux&&(p.Layer=="Auxiliary"||p.Layer=="Patrol")||!showAux&&(p.Kind=="Marker"||p.Kind=="EditorTrigger"))return false;
            if(p.Kind=="Terrain")return false;
            if(p.Kind=="Roof")return showRoofs&&category=="Roof";
            return true;
        }
        Placement PickPlacement(Cell cell)=>Map.Objects.LastOrDefault(p=>Selectable(p)&&HitPlacement(p,cell));
        void SelectPlacement(Placement hit,bool additive){
            if(selectedId!=hit.Id)rotationSignature=null;
            toggleOnRelease=additive&&selection.Contains(hit.Id)?hit.Id:null;
            if(!additive&&!selection.Contains(hit.Id))selection.Clear();
            selection.Add(hit.Id);selectedId=hit.Id;
        }
        void SelectBox(Cell start,Cell end){
            rotationSignature=null;
            int x1=Math.Min(start.X,end.X),x2=Math.Max(start.X,end.X),z1=Math.Min(start.Z,end.Z),z2=Math.Max(start.Z,end.Z);
            foreach(var p in EditableObjects().Where(Selectable)){
                var bounds=PlacementBounds(p);
                if(bounds.maxX<x1||bounds.minX>x2||bounds.maxZ<z1||bounds.minZ>z2)continue;
                selection.Add(p.Id);selectedId=p.Id;
            }
            status=RimCampaign.Runtime.RcText.T("已选中 ")+SelectedEditable().Count+RimCampaign.Runtime.RcText.T(" 个对象；拖动移动，R 整组旋转，Shift+R 原地转向。");
        }
        static CellRect PlacementBounds(Placement p){
            var def=p.Kind=="Thing"?DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name):null;
            return GenAdj.OccupiedRect(new IntVec3(p.Position.X,0,p.Position.Z),new Rot4(p.Rotation),def?.size??IntVec2.One);
        }
        void BeginSelectionMove(Cell cell){
            origin=cell;dragging=true;selectionMoved=false;
            originalPositions=SelectedEditable().ToDictionary(p=>p.Id,p=>Json.Copy(p.Position));
        }
        void PreviewSelectionMove(Cell cell){
            var objects=EditableObjects().Where(p=>originalPositions.ContainsKey(p.Id)).ToList();
            if(objects.Count==0)return;
            foreach(var p in objects)p.Position=Json.Copy(originalPositions[p.Id]);
            var bounds=objects.Select(PlacementBounds).ToList();
            int dx=Mathf.Clamp(cell.X-origin.X,-bounds.Min(b=>b.minX),Map.Width-1-bounds.Max(b=>b.maxX));
            int dz=Mathf.Clamp(cell.Z-origin.Z,-bounds.Min(b=>b.minZ),Map.Height-1-bounds.Max(b=>b.maxZ));
            selectionMoved|=dx!=0||dz!=0;
            foreach(var p in objects){p.Position.X+=dx;p.Position.Z+=dz;SetEditablePosition(p.Id,p.Position);}
            InvalidateScene();
        }
        void FinishSelectionMove(){
            var positions=EditableObjects().Where(p=>originalPositions.ContainsKey(p.Id)).ToDictionary(p=>p.Id,p=>Json.Copy(p.Position));
            foreach(var pair in originalPositions)SetEditablePosition(pair.Key,pair.Value);
            if(selectionMoved)Edit(_=>{foreach(var pair in positions)SetEditablePosition(pair.Key,pair.Value);});
            else if(toggleOnRelease!=null){selection.Remove(toggleOnRelease);selectedId=selection.LastOrDefault()??"";}
            dragging=false;toggleOnRelease=null;originalPositions.Clear();
            status=RimCampaign.Runtime.RcText.T("已选中 ")+SelectedEditable().Count+RimCampaign.Runtime.RcText.T(" 个对象");
        }
        void RotateSelection(bool individually=false){
            var originals=SelectedEditable();if(originals.Count==0)return;
            bool continued=rotationOriginals!=null&&rotationIndividual==individually&&rotationSignature==TransformSignature(originals);
            var baseline=continued?rotationOriginals:Json.Copy(originals);int step=continued?(rotationSteps+1)%4:1;
            var bounds=baseline.Select(PlacementBounds).ToList();
            var center=new Vector2((bounds.Min(b=>b.minX)+bounds.Max(b=>b.maxX))*.5f,(bounds.Min(b=>b.minZ)+bounds.Max(b=>b.maxZ))*.5f);
            var next=Json.Copy(baseline);
            foreach(var p in next){
                if(step==0)continue;
                var before=PlacementBounds(p);var desired=new Vector2((before.minX+before.maxX)*.5f,(before.minZ+before.maxZ)*.5f);
                if(!individually){var delta=desired-center;for(int i=0;i<step;i++)delta=new Vector2(delta.y,-delta.x);desired=center+delta;}
                p.Rotation=(p.Rotation+step)%4;p.Position=new Cell();var zero=PlacementBounds(p);
                p.Position=new Cell{X=(int)Math.Round(desired.x-(zero.minX+zero.maxX)*.5f,MidpointRounding.AwayFromZero),Z=(int)Math.Round(desired.y-(zero.minZ+zero.maxZ)*.5f,MidpointRounding.AwayFromZero)};
            }
            if(next.Select(PlacementBounds).Any(b=>b.minX<0||b.minZ<0||b.maxX>=Map.Width||b.maxZ>=Map.Height)){
                status=RimCampaign.Runtime.RcText.T("旋转后会超出地图边界；整组保持原位。");return;
            }
            var replacements=next.ToDictionary(p=>p.Id);
            EditPlacements(next,next.Where(p=>p.Kind=="EditorTrigger").Select(p=>p.Id),()=>{foreach(var p in replacements.Values){SetEditablePosition(p.Id,p.Position);var placed=Map.Objects.FirstOrDefault(o=>o.Id==p.Id);if(placed!=null)placed.Rotation=p.Rotation;}},true);
            rotationOriginals=baseline;rotationSteps=step;rotationIndividual=individually;rotationSignature=TransformSignature(SelectedEditable());
            status=individually?RcText.T("已按各自占地中心旋转 ")+next.Count+RcText.T(" 个对象"):RcText.T("已围绕整组占地中心旋转 ")+next.Count+RcText.T(" 个对象");
        }
        void DrawSelectionBox(){
            if(!boxSelecting||boxStart==null||boxEnd==null)return;
            int x1=Math.Min(boxStart.X,boxEnd.X),x2=Math.Max(boxStart.X,boxEnd.X),z1=Math.Min(boxStart.Z,boxEnd.Z),z2=Math.Max(boxStart.Z,boxEnd.Z);
            var rect=new Rect(canvasOffset.x+x1*zoom,canvasOffset.y+(Map.Height-1-z2)*zoom,(x2-x1+1)*zoom,(z2-z1+1)*zoom);
            Widgets.DrawBoxSolid(rect,new Color(.2f,.8f,1f,.12f));SelectionBorder(rect);
        }
    }
}
