using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public partial class Workspace
    {
        string patrolRoute="";bool patrolCheckpoint=true;
        IEnumerable<PatrolRoute> AllPatrolRoutes=>Map.Stealth.Routes.Concat(Map.PatrolRoutes);
        void PatrolMenu()
        {
            var choices=new List<FloatMenuOption>{
                new FloatMenuOption(RimCampaign.Runtime.RcText.T("创建巡逻路线并在地图点选路点"),()=>Prompt(RimCampaign.Runtime.RcText.T("路线名称"),RimCampaign.Runtime.RcText.T("巡逻路线"),name=>{
                    var route=new PatrolRoute{Name=name};Edit(_=>{if(Map.Components.Any(c=>c.Type=="RimCampaign.Runtime.StealthMapComponent"))Map.Stealth.Routes.Add(route);else Map.PatrolRoutes.Add(route);});patrolRoute=route.Id;tool="PatrolPoint";brush=null;status=RimCampaign.Runtime.RcText.T("先绘制路线，稍后绑定角色。路点可拖动、Shift 多选／框选、整组移动与撤销。");
                })),
                new FloatMenuOption(RimCampaign.Runtime.RcText.T("当前路点类型：")+(patrolCheckpoint?RimCampaign.Runtime.RcText.T("检查点（停留）"):RimCampaign.Runtime.RcText.T("经过点（不停留）")),()=>patrolCheckpoint=!patrolCheckpoint),
                new FloatMenuOption(RimCampaign.Runtime.RcText.T("继续添加已有路线的路点"),()=>EditorUI.Pick(AllPatrolRoutes.Select(r=>new KeyValuePair<string,string>(r.Id,r.Name)),id=>{patrolRoute=id;tool="PatrolPoint";brush=null;})),
                new FloatMenuOption(RimCampaign.Runtime.RcText.T("编辑路线（顺序、停留、朝向、往返）"),()=>EditorUI.Pick(AllPatrolRoutes.Select(r=>new KeyValuePair<string,string>(r.Id,r.Name)),id=>{
                    var route=AllPatrolRoutes.First(r=>r.Id==id);
                    Find.WindowStack.Add(new ObjectWindow(route,value=>Edit(_=>{
                        var updated=(PatrolRoute)value;var list=Map.Stealth.Routes.Contains(route)?Map.Stealth.Routes:Map.PatrolRoutes;
                        list[list.IndexOf(route)]=updated;
                        foreach(var point in updated.Points){var marker=Map.Objects.FirstOrDefault(o=>o.Id==point.Marker);if(marker!=null){marker.Position=Json.Copy(point.Position);marker.Rotation=point.Facing;}}
                    }),Project,Map));
                })),
                new FloatMenuOption(RimCampaign.Runtime.RcText.T("把路线绑定到所选角色（可多选）"),()=>EditorUI.Pick(AllPatrolRoutes.Select(r=>new KeyValuePair<string,string>(r.Id,r.Name)),BindPatrol)),
                new FloatMenuOption(RimCampaign.Runtime.RcText.T("解除所选角色的巡逻绑定"),()=>Edit(_=>{var ids=SelectedPlacements().Select(p=>p.Id).ToList();Map.CombatPatrols.RemoveAll(p=>ids.Contains(p.Pawn));foreach(var guard in Map.Stealth.Guards.Where(g=>ids.Contains(g.Pawn)))guard.Route="";})),

            };
            Find.WindowStack.Add(new FloatMenu(choices));
        }
        void AddPatrolPoint(Cell cell)
        {
            var route=AllPatrolRoutes.FirstOrDefault(r=>r.Id==patrolRoute);
            if(route==null){tool="";return;}
            Edit(_=>{var marker=new Placement{Kind="Marker",Layer="Patrol",Name=route.Name+RimCampaign.Runtime.RcText.T(" / 路点 ")+(route.Points.Count+1),Position=Json.Copy(cell)};Map.Objects.Add(marker);route.Points.Add(new PatrolPoint{Marker=marker.Id,Position=cell,Checkpoint=patrolCheckpoint,WaitTicks=patrolCheckpoint?180:0});});status=route.Name+RimCampaign.Runtime.RcText.T("：已添加第 ")+route.Points.Count+RimCampaign.Runtime.RcText.T(" 个路点（右键结束）。");
        }
        void BindPatrol(string routeId){
            var pawns=SelectedPlacements().Where(p=>new[]{"PawnKind","Pawn","Boss"}.Contains(p.Kind)).ToList();
            if(pawns.Count==0){status=RimCampaign.Runtime.RcText.T("先在地图选择一个或多个巡逻角色，再绑定路线。");return;}
            Edit(_=>{foreach(var pawn in pawns)AssignPatrol(routeId,pawn);});status=RimCampaign.Runtime.RcText.T("已绑定 ")+pawns.Count+RimCampaign.Runtime.RcText.T(" 名角色；路线不依赖宿主的位置。");
        }
        void AssignPatrol(string routeId,Placement pawn){
                if(Map.Stealth.Routes.Any(r=>r.Id==routeId)){var guard=Map.Stealth.Guards.FirstOrDefault(g=>g.Pawn==pawn.Id);if(guard==null){guard=new GuardSpec{Pawn=pawn.Id,Name=pawn.Name};Map.Stealth.Guards.Add(guard);}guard.Route=routeId;}
                else{Map.CombatPatrols.RemoveAll(p=>p.Pawn==pawn.Id);Map.CombatPatrols.Add(new CombatPatrolSpec{Pawn=pawn.Id,Route=routeId,Name=pawn.Name});}
        }
        void DrawStealthOverlay()
        {
            Vector2 At(Cell c)=>new Vector2(canvasOffset.x+(c.X+.5f)*zoom,canvasOffset.y+(Map.Height-c.Z-.5f)*zoom);
            void Cone(Cell position,int facing,float radius,float angle){
                var center=At(position);float start=(facing*90-90-angle/2)*Mathf.Deg2Rad;
                Vector2 Point(float a)=>center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius*zoom;
                if(angle<360){Widgets.DrawLine(center,Point(start),Color.cyan,1);Widgets.DrawLine(center,Point(start+angle*Mathf.Deg2Rad),Color.cyan,1);}
                for(int i=0;i<64;i++)Widgets.DrawLine(Point(start+i*angle*Mathf.Deg2Rad/64),Point(start+(i+1)*angle*Mathf.Deg2Rad/64),new Color(0,1,1,.6f),1);
            }
            foreach(var guard in Map.Stealth.Guards){var pawn=FindPlacement(guard.Pawn);if(pawn!=null&&IsSelected(pawn.Id))Cone(pawn.Position,pawn.Rotation,guard.SightRange,guard.ViewAngle);}
            foreach(var device in Map.Stealth.Devices.Where(d=>d.Kind=="Camera"&&IsSelected(d.Host))){var host=FindPlacement(device.Host);Cone(host?.Position??device.Position,device.Facing,device.SightRange,device.ViewAngle);}
            Cell Position(PatrolPoint point)=>FindPlacement(point.Marker)?.Position??point.Position;
            foreach(var route in AllPatrolRoutes){
                bool related=IsSelected(route.Id)||RelationsFor(selectedId).Any(link=>link.Target==route.Id);
                var routeColor=related?new Color(.3f,1,1):new Color(1,.85f,.18f,.5f);
                for(int i=0;i<route.Points.Count;i++){
                    var at=At(Position(route.Points[i]));
                    if(i>0)Widgets.DrawLine(At(Position(route.Points[i-1])),at,routeColor,related?3:1);
                    Widgets.DrawBoxSolid(new Rect(at.x-3,at.y-3,6,6),routeColor);
                    var label=new Rect(at.x+4,at.y-12,110,24);if(zoom>=6)EditorUI.DrawLabel(label,(i+1).ToString()+(route.Points[i].Checkpoint?" ⏱ "+(route.Points[i].WaitTicks/60f).ToString("0.#")+"s":""));TooltipHandler.TipRegion(label,route.Name+" / "+EditorUI.Label(route.Points[i]));
                }
                if(!route.PingPong&&route.Points.Count>1)Widgets.DrawLine(At(Position(route.Points.Last())),At(Position(route.Points[0])),routeColor,related?3:1);
            }
            foreach(var d in Map.Stealth.Devices){
                var cell=FindPlacement(d.Host)?.Position??d.Position;var at=At(cell);
                var box=new Rect(at.x-7,at.y-7,14,14);Widgets.DrawBoxSolid(box,new Color(.2f,.8f,1,.7f));TooltipHandler.TipRegion(box,d.Name+" ["+d.Kind+"]");
            }
        }
    }
}
