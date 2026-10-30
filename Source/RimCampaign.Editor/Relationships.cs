using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor {
    public partial class Workspace {
        sealed class EditorRelation { public string Target,Label; }
        readonly Dictionary<string,List<EditorRelation>> relations=new Dictionary<string,List<EditorRelation>>();
        readonly List<Tuple<string,List<Placement>>> taskPaths=new List<Tuple<string,List<Placement>>>();
        string inspectorTab="Overview";Vector2 overviewScroll;
        void RemovePatrolReferences(HashSet<string> ids){
            foreach(var route in AllPatrolRoutes.ToList()){
                if(ids.Contains(route.Id))foreach(var point in route.Points)ids.Add(point.Marker);
                route.Points.RemoveAll(p=>ids.Contains(p.Marker));
                if(route.Points.Count==0)ids.Add(route.Id);
            }
            Map.PatrolRoutes.RemoveAll(r=>ids.Contains(r.Id));Map.Stealth.Routes.RemoveAll(r=>ids.Contains(r.Id));
            Map.CombatPatrols.RemoveAll(b=>ids.Contains(b.Pawn)||ids.Contains(b.Route));
            Map.Stealth.Guards.RemoveAll(g=>ids.Contains(g.Pawn));
            foreach(var guard in Map.Stealth.Guards)if(ids.Contains(guard.Route))guard.Route="";
        }
        void RebuildRelations(){
            relations.Clear();taskPaths.Clear();
            void Link(string source,string target,string label){
                if(string.IsNullOrEmpty(source)||string.IsNullOrEmpty(target))return;
                if(!relations.TryGetValue(source,out var links))relations[source]=links=new List<EditorRelation>();
                if(!links.Any(x=>x.Target==target))links.Add(new EditorRelation{Target=target,Label=label});
            }
            void Pair(string a,string b,string forward,string back){Link(a,b,forward);Link(b,a,back);}
            foreach(var route in AllPatrolRoutes){
                foreach(var point in route.Points)Pair(point.Marker,route.Id,"所属巡逻路线","路点");
                var members=Map.CombatPatrols.Where(b=>b.Route==route.Id).Select(b=>b.Pawn).Concat(Map.Stealth.Guards.Where(g=>g.Route==route.Id).Select(g=>g.Pawn));
                foreach(var pawn in members){Pair(route.Id,pawn,"绑定角色","巡逻路线");foreach(var point in route.Points)Link(point.Marker,pawn,"本路线绑定角色");}
            }
            foreach(var item in Map.Interactions)Pair(item.Host,item.Id,"可执行交互","操作墙体 / 建筑");
            foreach(var tree in Map.Groups){
                var actor=Map.Objects.FirstOrDefault(o=>o.Id==tree.Actor||(o.Kind=="Pawn"&&(o.Role==tree.Actor||"role:"+o.Role==tree.Actor)));
                if(actor!=null)Pair(tree.Id,actor.Id,"任务执行单位 / Task actor","任务树 / Task tree");
                var points=new List<Placement>();
                foreach(var a in Validation.Actions(tree.Actions))if(sceneById.TryGetValue(a.Target,out var target)){
                    Pair(tree.Id,target.Id,"任务目标 / Task target","任务树 / Task tree");
                    if(a.Kind=="rc.task.move"&&target is Placement point){points.Add(point);if(actor!=null)Pair(point.Id,actor.Id,"任务执行单位 / Task actor","任务路点 / Task waypoint");}
                }
                if(points.Count>0)taskPaths.Add(Tuple.Create(tree.Id,points));
            }
            foreach(var rule in Map.Rules){
                if(!string.IsNullOrEmpty(rule.Host))Pair(rule.Id,rule.Host,"检测宿主","相关触发器");
                foreach(var e in rule.Events)if(sceneById.ContainsKey(e.Target))Pair(rule.Id,e.Target,"事件来源","相关触发器");
                foreach(var a in Validation.Actions(rule.Actions))if(sceneById.ContainsKey(a.Target))Pair(rule.Id,a.Target,"动作目标","相关触发器");
            }
            foreach(var item in sceneById.Values)foreach(var id in item.RelatedObjects??new List<string>())Pair(item.Id,id,"作者关联","作者关联");
        }
        string RelationName(string id){if(!sceneById.TryGetValue(id,out var value))return RcText.T("引用已失效：")+id;string name=RcText.T(string.IsNullOrEmpty(value.Name)?value.Id:value.Name);return value is Placement p?name+" ("+p.Position.X+", "+p.Position.Z+")":name;}
        List<EditorRelation> RelationsFor(string id){EnsureSceneIndex();return relations.TryGetValue(id,out var links)?links:new List<EditorRelation>();}
        void DrawOverview(Named obj,Rect area){
            var links=RelationsFor(obj.Id);var lines=new List<string>();
            if(obj is Placement p){
                lines.Add(p.Layer=="Patrol"?RcText.T("巡逻路点；本身不生成游戏物体。延伸的黄色线段表示路径。"):p.Kind=="PawnKind"||p.Kind=="Pawn"||p.Kind=="Boss"?RcText.T("角色放置；下方可查看并跳转巡逻路线。"):p.Kind=="Marker"?RcText.T("定位标记；本身不生成游戏物体。"):RcText.T("地图实体"));
                lines.Add(RcText.T("位置：")+p.Position.X+", "+p.Position.Z);
                if(p.Layer=="Patrol"&&!links.Any(x=>x.Label=="本路线绑定角色"))lines.Add(RcText.T("此路线尚未绑定角色，不会驱动巡逻。"));
                if(p.Kind=="PawnKind"||p.Kind=="Boss")lines.Add(RcText.T("开局生成：")+(p.SpawnAtStart?RcText.T("是"):RcText.T("否，由剧情生成")));
            }
            if(obj is PatrolRoute route)lines.Add(RcText.T("路点数量：")+route.Points.Count+" · "+(route.PingPong?RcText.T("往返"):RcText.T("循环")));
            if(obj is ActionGroup tree){lines.Add("任务绑定 / Actor: "+EditorUI.Reference(Map,tree.Actor));lines.AddRange(tree.Actions.Select(a=>EditorUI.Describe(a,Map)));}
            if(obj is Rule rule){lines.Add(RcText.T("触发：")+string.Join(" / ",rule.Events.Select(e=>EditorUI.Describe(e,Map))));lines.Add(RcText.T("条件：")+EditorUI.Describe(rule.When,Map));lines.AddRange(rule.Actions.Select(a=>EditorUI.Describe(a,Map)));}
            if(obj is SceneInteractionSpec spec){
                lines.Add(RcText.T("玩家操作：")+RcText.T(spec.Label));lines.Add(RcText.T("条件：")+EditorUI.Describe(spec.When,Map));
                if(spec.BlockCells.Count>0)lines.Add(RcText.T("封路格数：")+spec.BlockCells.Count+" · "+spec.ObstacleDef);
                lines.AddRange(spec.Actions.Select(a=>EditorUI.Describe(a,Map)));
            }
            if(!string.IsNullOrWhiteSpace(obj.Notes))lines.Insert(0,RcText.T(obj.Notes));
            else lines.Insert(0,RcText.T("暂无备注；可在属性中补充用途说明。"));
            string description=string.Join("\n\n",lines);var old=Text.Font;Text.Font=GameFont.Tiny;
            float height=Text.CalcHeight(description,area.width-22)+12;
            Widgets.BeginScrollView(area,ref overviewScroll,new Rect(0,0,area.width-18,height+links.Count*38+90));
            EditorUI.DrawLabel(new Rect(0,0,area.width-24,height),description);
            if(obj is PatrolRoute selectedRoute){
                if(EditorUI.Button(new Rect(0,height,area.width-24,30),RcText.T("绑定角色到此路线…")))EditorUI.Pick(Map.Objects.Where(o=>new[]{"Pawn","PawnKind","Boss"}.Contains(o.Kind)).Select(o=>new KeyValuePair<string,string>(o.Id,o.Name)),id=>Edit(_=>AssignPatrol(selectedRoute.Id,Map.Objects.Single(o=>o.Id==id))));height+=36;
            }else if(obj is Placement actor&&new[]{"Pawn","PawnKind","Boss"}.Contains(actor.Kind)){
                if(EditorUI.Button(new Rect(0,height,area.width-24,30),RcText.T("设置巡逻路线…")))EditorUI.Pick(AllPatrolRoutes.Select(r=>new KeyValuePair<string,string>(r.Id,r.Name)),id=>Edit(_=>AssignPatrol(id,Map.Objects.Single(o=>o.Id==actor.Id))));height+=36;
            }
            EditorUI.DrawLabel(new Rect(0,height,area.width-24,30),RcText.T("关联对象（点击定位）"));height+=34;
            foreach(var link in links){
                bool binding=obj is PatrolRoute&&link.Label=="绑定角色";
                if(EditorUI.Button(new Rect(0,height,area.width-(binding?60:24),34),RcText.T(link.Label)+" · "+RelationName(link.Target))&&sceneById.TryGetValue(link.Target,out var target)){FocusObject(target);overviewScroll=Vector2.zero;}
                if(binding&&EditorUI.Button(new Rect(area.width-56,height,32,34),"×"))Edit(_=>{Map.CombatPatrols.RemoveAll(b=>b.Pawn==link.Target&&b.Route==obj.Id);foreach(var g in Map.Stealth.Guards.Where(g=>g.Pawn==link.Target&&g.Route==obj.Id))g.Route="";});
                height+=38;
            }
            Widgets.EndScrollView();Text.Font=old;
        }
        void DrawActorMarker(Placement pawn,Rect cell){
            var old=GUI.color;GUI.color=Color.white;
            float side=Math.Max(9,cell.width);var r=new Rect(cell.center.x-side/2,cell.center.y-side/2,side,side);
            var faction=Map.Factions.FirstOrDefault(f=>f.Id==pawn.Faction);
            var color=pawn.Faction=="Player"||faction?.Side=="Ally"?new Color(.25f,.8f,1):faction?.Side=="Neutral"?new Color(.9f,.85f,.55f):new Color(1,.38f,.24f);
            Widgets.DrawBoxSolid(r,new Color(.08f,.1f,.12f,1));
            Widgets.DrawBoxSolid(new Rect(r.x+side*.34f,r.y+side*.1f,side*.32f,side*.32f),color);
            Widgets.DrawBoxSolid(new Rect(r.x+side*.2f,r.y+side*.46f,side*.6f,side*.43f),color);
            if(RelationsFor(selectedId).Any(link=>link.Target==pawn.Id)){GUI.color=Color.cyan;Widgets.DrawBox(r.ExpandedBy(2),2);GUI.color=Color.white;}
            var direction=pawn.Rotation==0?Vector2.up:pawn.Rotation==1?Vector2.right:pawn.Rotation==2?Vector2.down:Vector2.left;
            direction.y=-direction.y;Widgets.DrawLine(r.center,r.center+direction*side*.7f,color,2);
            if(zoom>=22)EditorUI.DrawLabel(new Rect(r.xMax+3,r.y,150,24),pawn.Name);
            TooltipHandler.TipRegion(r,RcText.T(pawn.Name)+"\n"+RcText.T("角色放置；选择后查看巡逻绑定"));GUI.color=old;
        }
        void DrawInteractionOverlay(){
            foreach(var path in taskPaths){
                bool chosen=IsSelected(path.Item1)||path.Item2.Any(p=>IsSelected(p.Id))||RelationsFor(selectedId).Any(r=>r.Target==path.Item1);
                if(!chosen)continue;
                Vector2? previous=null;foreach(var point in path.Item2){var at=new Vector2(canvasOffset.x+(point.Position.X+.5f)*zoom,canvasOffset.y+(Map.Height-point.Position.Z-.5f)*zoom);if(previous.HasValue)Widgets.DrawLine(previous.Value,at,Color.cyan,2);previous=at;}
            }
            foreach(var item in Map.Interactions){
                var host=FindPlacement(item.Host);if(host==null)continue;
                var center=new Vector2(canvasOffset.x+(host.Position.X+.5f)*zoom,canvasOffset.y+(Map.Height-host.Position.Z-.5f)*zoom);
                float side=Math.Max(10,zoom);var rect=new Rect(center.x-side/2,center.y-side/2,side,side);
                bool chosen=IsSelected(host.Id)||IsSelected(item.Id)||RelationsFor(selectedId).Any(x=>x.Target==item.Id||x.Target==host.Id);
                Color color=item.BlockCells.Count>0?new Color(1,.60f,.16f,.95f):new Color(.3f,1,.75f,.95f);
                var old=GUI.color;GUI.color=color;Widgets.DrawBox(rect,chosen?3:2);GUI.color=old;
                TooltipHandler.TipRegion(rect,RcText.T(item.Label)+"\n"+RcText.T(host.Name)+"\n"+RcText.T(item.Notes??""));
                if(!chosen)continue;
                foreach(var cell in item.BlockCells){var block=new Rect(canvasOffset.x+cell.X*zoom,canvasOffset.y+(Map.Height-1-cell.Z)*zoom,zoom,zoom);Widgets.DrawBoxSolid(block,new Color(1,.52f,.1f,.35f));}
            }
        }
    }
}
