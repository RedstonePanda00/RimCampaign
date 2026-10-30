using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using UnityEngine;
using Verse;
using Region=RimCampaign.Core.Region;

namespace RimCampaign.Editor {
    public partial class Workspace {
        // These short-lived handles exist only in the editor, never in Map.Objects.
        IEnumerable<Placement> EditableObjects()=>Map.Objects.Concat(Map.Rules.Where(TriggerGeometry.Visible).Select(r=>new Placement{Id=r.Id,Name=r.Name,Kind="EditorTrigger",Layer="Auxiliary",Position=Json.Copy(r.Position)}));
        List<Placement> SelectedEditable()=>EditableObjects().Where(p=>IsSelected(p.Id)).ToList();
        void SetEditablePosition(string id,Cell position){
            var rule=Map.Rules.FirstOrDefault(r=>r.Id==id);
            if(rule!=null){rule.Position=Json.Copy(position);return;}
            var p=Map.Objects.FirstOrDefault(o=>o.Id==id);if(p!=null)p.Position=Json.Copy(position);
        }
        Placement PickEditable(Cell cell)=>EditableObjects().LastOrDefault(p=>Selectable(p)&&HitPlacement(p,cell));
        void InspectSelection(string id){selectedId=id;selection.Clear();selection.Add(id);rightOpen=true;}
        void FocusObject(Named obj){
            SwitchWorkspace("Map");InspectSelection(obj.Id);
            var point=(obj as Placement)?.Position??(obj as Rule)?.Position??(obj as Region)?.Min;
            if(obj is PatrolRoute route)point=route.Points.FirstOrDefault()?.Position;
            if(obj is SceneInteractionSpec interaction)point=FindPlacement(interaction.Host)?.Position;
            if(point!=null){if(zoom<6)mapView.ZoomAt(-(float)Math.Ceiling(Math.Log(6/zoom)/Math.Log(1.15)),mapView.ViewWidth/2,mapView.ViewHeight/2);mapView.LookAt(point.X+.5f,Map.Height-point.Z-.5f);}
        }
        void DrawTriggerOverlay(){
            foreach(var rule in Map.Rules.Where(TriggerGeometry.Visible)){
                var center=new Vector2(canvasOffset.x+(rule.Position.X+.5f)*zoom,canvasOffset.y+(Map.Height-rule.Position.Z-.5f)*zoom);
                float side=Math.Max(8,zoom);var rect=new Rect(center.x-side/2,center.y-side/2,side,side);
                Widgets.DrawBoxSolid(rect,new Color(.23f,.61f,.84f,.9f));
                if(zoom>=12)EditorUI.DrawLabel(rect,rule.Events.Any(e=>e.Kind=="Periodic")?"R":"T");
                TooltipHandler.TipRegion(rect,rule.Name+RimCampaign.Runtime.RcText.T("\n触发器（仅编辑器可选）"));
                if(!IsSelected(rule.Id))continue;
                SelectionBorder(rect);
                var host=FindPlacement(rule.Host)??drawableObjects.FirstOrDefault(o=>o.Kind=="Pawn"&&rule.Host=="role:"+o.Role);
                if(host!=null)center=new Vector2(canvasOffset.x+(host.Position.X+.5f)*zoom,canvasOffset.y+(Map.Height-host.Position.Z-.5f)*zoom);
                if(!TriggerGeometry.Spatial(rule))continue;
                if(rule.RangeShape=="Rectangle"){
                    var area=new Rect(center.x-rule.RangeWidth*zoom/2,center.y-rule.RangeHeight*zoom/2,rule.RangeWidth*zoom,rule.RangeHeight*zoom);
                    Widgets.DrawBoxSolid(area,new Color(.2f,.65f,.9f,.09f));SelectionBorder(area);
                }else for(int i=0;i<64;i++){
                    float a=i*Mathf.PI/32,b=(i+1)*Mathf.PI/32;
                    Widgets.DrawLine(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*rule.Radius*zoom,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*rule.Radius*zoom,SelectionColor,1);
                }
            }
        }
    }
}
