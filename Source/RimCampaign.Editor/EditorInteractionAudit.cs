using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor {
    // Run only by the explicit native hot-test audit, on its isolated project copy.
    static class EditorInteractionAudit {
        internal static string TriggerId;
        internal static void Readability(){
            var w=new Workspace();var t=Traverse.Create(w);var map=w.Map;
            map.Objects.Add(new Placement{Id="read-pawn",Kind="PawnKind",Name="Test guard"});
            map.Objects.Add(new Placement{Id="read-point",Kind="Marker",Layer="Patrol",Name="Test point",Notes="A useful multiline note\nSecond line"});
            map.PatrolRoutes.Add(new PatrolRoute{Id="read-route",Name="Test route",Points=new List<PatrolPoint>{new PatrolPoint{Marker="read-point"}}});
            map.CombatPatrols.Add(new CombatPatrolSpec{Pawn="read-pawn",Route="read-route"});
            map.Objects.Add(new Placement{Id="read-wall",Kind="Thing",Name="Operating wall"});
            map.Interactions.Add(new SceneInteractionSpec{Id="read-block",Host="read-wall",Name="Collapse",RelatedObjects=new List<string>{"read-point"}});
            t.Method("EnsureSceneIndex").GetValue();
            var links=(System.Collections.IDictionary)t.Field("relations").GetValue();
            HotTestAudit.Check(links.Contains("read-pawn")&&links.Contains("read-point")&&links.Contains("read-wall")&&links.Contains("read-block"),"relationship index covers route members, points and interaction hosts");
            HotTestAudit.Check(t.Method("FindSceneItem","read-route").GetValue() is PatrolRoute&&t.Method("FindSceneItem","read-block").GetValue() is SceneInteractionSpec,"routes and interactions can be selected and inspected directly");
            var marker=map.Objects.Single(x=>x.Id=="read-point");
            HotTestAudit.Check(!EditorUI.RelevantField(marker,"Stuff")&&!EditorUI.RelevantField(marker,"Count")&&!EditorUI.RelevantField(marker,"BindRole")&&EditorUI.RelevantField(marker,"Notes"),"waypoint properties omit material count and role inheritance, retain notes");
            HotTestAudit.Check(Json.Copy(marker).Notes==marker.Notes&&Json.Copy(map.Interactions[0]).RelatedObjects.Single()=="read-point","notes and author links round-trip without loss");
            HotTestAudit.Check(EditorUI.Describe(map.CombatPatrols[0],map)=="Test guard → Test route","patrol binding caption resolves both human-readable names");
            t.Method("RemoveSelection",new HashSet<string>{"read-pawn"}).GetValue();
            HotTestAudit.Check(w.Map.CombatPatrols.Count==0&&w.Map.PatrolRoutes.Count==1,"deleting a pawn removes its binding and preserves the independent route");
            t.Field("history").GetValue<History<Project>>().Undo();
            HotTestAudit.Check(w.Map.CombatPatrols.Count==1,"binding deletion is undoable");
            t.Method("RemoveSelection",new HashSet<string>{"read-route"}).GetValue();
            HotTestAudit.Check(w.Map.PatrolRoutes.Count==0&&w.Map.CombatPatrols.Count==0&&w.Map.Objects.All(x=>x.Id!="read-point"),"deleting a route removes its points and bindings together");
        }
        internal static void PointerSelection(){
            var w=new Workspace();var t=Traverse.Create(w);var map=w.Map;map.Width=map.Height=20;
            map.Objects.Add(new Placement{Id="box-wall",Kind="Thing",Def=new DefRef{Name="Wall"},Position=new Cell{X=5,Z=5}});
            map.Objects.Add(new Placement{Id="box-waypoint",Kind="Marker",Layer="Patrol",Position=new Cell{X=7,Z=6}});
            map.Objects.Add(new Placement{Id="outside-box",Kind="Marker",Position=new Cell{X=12,Z=12}});
            map.Rules.Add(new Rule{Id="box-trigger",Position=new Cell{X=8,Z=7},Events=new List<EventSpec>{new EventSpec{Kind="RangeEnter"}}});
            var view=t.Field("mapView").GetValue<MapViewport>();var rect=new Rect(0,0,400,400);view.Configure(map.Id,20,20,400,400);
            HashSet<string> Selected()=>t.Field("selection").GetValue<HashSet<string>>();
            void Input(EventType type,int x,int z,bool shift=false,int button=0){
                var e=new Event{type=type,button=button,clickCount=1,mousePosition=new Vector2(view.X+(x+.5f)*view.Zoom,view.Y+(19-z+.5f)*view.Zoom),modifiers=shift?EventModifiers.Shift:EventModifiers.None};
                var previous=Event.current;
                System.IO.File.AppendAllText(System.IO.Path.Combine(HotTestAudit.Folder,"pointer-trace.txt"),$"before {type} {x},{z} expectedShift={shift} actualShift={e.shift} modifiers={e.modifiers} selection={string.Join(",",Selected())}\n");
                try{Event.current=e;t.Method("HandleMapInput",rect,e).GetValue();}finally{Event.current=previous;}
                System.IO.File.AppendAllText(System.IO.Path.Combine(HotTestAudit.Folder,"pointer-trace.txt"),$"after selection={string.Join(",",Selected())} box={t.Field("boxSelecting").GetValue<bool>()}\n");
            }
            t.Field("selection").SetValue(new HashSet<string>{"outside-box"});t.Field("selectedId").SetValue("outside-box");
            Input(EventType.MouseDown,4,4);HotTestAudit.Check(t.Field("boxSelecting").GetValue<bool>(),"plain left press on empty canvas starts rectangle selection");
            Input(EventType.MouseDrag,9,9);Input(EventType.MouseUp,9,9);
            HotTestAudit.Check(Selected().SetEquals(new[]{"box-wall","box-waypoint","box-trigger"}),"unmodified drag rectangle selects wall, waypoint and trigger and replaces old selection");
            HotTestAudit.Check(map.Objects.Single(o=>o.Id=="box-wall").Position.X==5,"rectangle selection does not move objects");
            Input(EventType.MouseDown,11,11,true);Input(EventType.MouseDrag,13,13,true);Input(EventType.MouseUp,13,13,true);
            HotTestAudit.Check(Selected().Count==4&&Selected().Contains("outside-box"),"shift drag appends to rectangle selection (actual: "+string.Join(",",Selected())+")");
            Input(EventType.MouseDown,5,5);Input(EventType.MouseDrag,6,5);Input(EventType.MouseUp,6,5);
            HotTestAudit.Check(w.Map.Objects.Single(o=>o.Id=="box-wall").Position.X==6&&w.Map.Rules.Single(r=>r.Id=="box-trigger").Position.X==9,"dragging selected object still moves the whole group");
            t.Field("lockAux").SetValue(true);Input(EventType.MouseDown,4,4);Input(EventType.MouseDrag,11,10);Input(EventType.MouseUp,11,10);
            HotTestAudit.Check(Selected().SetEquals(new[]{"box-wall"}),"rectangle selection respects locked patrol and trigger layers");
            Input(EventType.MouseDown,0,0);Input(EventType.MouseDrag,35,35);Input(EventType.MouseUp,35,35);
            HotTestAudit.Check(!t.Field("boxSelecting").GetValue<bool>(),"release outside canvas finishes rectangle selection");
            Input(EventType.MouseDown,0,0);Input(EventType.MouseDown,1,1,false,1);
            HotTestAudit.Check(!t.Field("boxSelecting").GetValue<bool>(),"right click cancels rectangle gesture");
            Input(EventType.MouseDown,0,0);t.Method("CancelPointerGestures").GetValue();
            HotTestAudit.Check(!t.Field("boxSelecting").GetValue<bool>(),"focus loss cancellation clears rectangle gesture");
        }
        internal static void Authoring(Workspace workspace){
            var t=Traverse.Create(workspace);var history=t.Field("history").GetValue<History<Project>>();
            void Call(string name,params object[] args)=>t.Method(name,args).GetValue();
            void Select(params string[] ids){t.Field("selection").SetValue(new HashSet<string>(ids));t.Field("selectedId").SetValue(ids.LastOrDefault()??"");}
            Call("AddTrigger",new Cell{X=8,Z=10});TriggerId=workspace.Map.Rules.Last().Id;
            Call("AddTrigger",new Cell{X=10,Z=10});string second=workspace.Map.Rules.Last().Id;
            HotTestAudit.Check(workspace.Map.Rules.TakeLast(2).All(TriggerGeometry.Spatial)&&!workspace.Map.Objects.Any(o=>o.Id==TriggerId||o.Id==second),"spatial placement creates only rules, not runtime entities");
            Select(TriggerId,second);Call("BeginSelectionMove",new Cell{X=8,Z=10});Call("PreviewSelectionMove",new Cell{X=12,Z=13});Call("FinishSelectionMove");
            HotTestAudit.Check(workspace.Map.Rules.Single(r=>r.Id==TriggerId).Position.X==12&&workspace.Map.Rules.Single(r=>r.Id==second).Position.X==14,"two spatial triggers drag together");
            history.Undo();HotTestAudit.Check(workspace.Map.Rules.Single(r=>r.Id==TriggerId).Position.X==8,"trigger drag undo restores position");history.Redo();
            Select(TriggerId,second);Call("Copy");Call("Paste");var clones=t.Field("selection").GetValue<HashSet<string>>().ToList();
            HotTestAudit.Check(clones.Count==2&&clones.All(id=>workspace.Map.Rules.Any(r=>r.Id==id))&&!clones.Contains(TriggerId)&&workspace.Map.Rules.Single(r=>r.Id==TriggerId).Position.X==12,"trigger copy duplicates data with fresh identities");
            Call("RemoveSelection",new HashSet<string>(clones));HotTestAudit.Check(clones.All(id=>workspace.Map.Rules.All(r=>r.Id!=id)),"trigger delete removes canonical rule");history.Undo();HotTestAudit.Check(clones.All(id=>workspace.Map.Rules.Any(r=>r.Id==id)),"trigger deletion is undoable");history.Redo();
            Select(TriggerId);Call("BeginSelectionMove",new Cell{X=12,Z=13});Call("PreviewSelectionMove",new Cell{X=18,Z=15});Call("CancelPointerGestures");HotTestAudit.Check(workspace.Map.Rules.Single(r=>r.Id==TriggerId).Position.X==12,"cancelled drag restores trigger position");
            Select();Call("SelectBox",new Cell{X=11,Z=12},new Cell{X=15,Z=14});HotTestAudit.Check(t.Field("selection").GetValue<HashSet<string>>().Contains(TriggerId)&&t.Field("selection").GetValue<HashSet<string>>().Contains(second),"box selection includes spatial triggers");
            t.Field("lockAux").SetValue(true);var hit=t.Method("PickEditable",new Cell{X=12,Z=13}).GetValue<Placement>();HotTestAudit.Check(hit?.Id!=TriggerId,"locked trigger layer cannot be picked");t.Field("lockAux").SetValue(false);
            t.Field("showAux").SetValue(false);hit=t.Method("PickEditable",new Cell{X=12,Z=13}).GetValue<Placement>();HotTestAudit.Check(hit?.Id!=TriggerId,"hidden triggers cannot be picked");t.Field("showAux").SetValue(true);
            var rule=workspace.Map.Rules.Single(r=>r.Id==TriggerId);rule.Position=new Cell{X=140,Z=16};rule.RangeShape="Rectangle";rule.RangeWidth=12;rule.RangeHeight=8;rule.Once=false;
            rule.Events=new List<EventSpec>{new EventSpec{Kind="RangeEnter"},new EventSpec{Kind="RangeLeave"}};
            Call("FocusObject",rule);HotTestAudit.Check(ReferenceEquals(rule,workspace.Map.Rules.Single(r=>r.Id==TriggerId))&&t.Field("selectedId").GetValue<string>()==TriggerId,"story locator selects the same canonical map rule");
            var nonspatial=new Rule{Id="nonspatial-probe",Name="nonspatial",Events=new List<EventSpec>{new EventSpec{Kind="Signal"}}};workspace.Map.Rules.Add(nonspatial);
            HotTestAudit.Check(t.Method("EditableObjects").GetValue<IEnumerable<Placement>>().All(p=>p.Id!=nonspatial.Id),"nonspatial logic has no map handle");
        }
        internal static void Runtime(CampaignController c){
            var actor=c.Actors["hero"];var rule=c.MapSpec.Rules.Single(r=>r.Id==TriggerId);var snapshot=Json.Copy(rule);string id=actor.ThingID;
            HotTestAudit.Check(!c.Objects.ContainsKey(TriggerId)&&c.Map.listerThings.AllThings.All(t=>t.ThingID!=TriggerId),"runtime spatial trigger creates no selectable Thing");
            rule.Position=new Cell{X=actor.Position.x,Z=actor.Position.z};rule.RangeWidth=4;rule.RangeHeight=4;rule.Detect="Specific";rule.Subject="role:hero";
            c.State.Signals.RemoveAll(s=>s.RuleId==rule.Id);c.State.Triggers.Remove(rule.Id);c.PollTriggers();
            System.IO.File.WriteAllText(System.IO.Path.Combine(HotTestAudit.Folder,"trigger-probe.txt"),"phase="+c.State.Phase+" enabled="+rule.Enabled+" override="+(c.State.Enabled.TryGetValue(rule.Id,out var enabled)?enabled.ToString():"none")+" host="+rule.Host+" actor="+actor.Position+" spawned="+actor.Spawned+" events="+string.Join(",",c.State.Signals.Where(s=>s.RuleId==rule.Id).Select(s=>s.Kind+":"+s.Source)));
            HotTestAudit.Check(c.State.Signals.Count(s=>s.RuleId==rule.Id&&s.Kind=="RangeEnter")==1,"rectangle detects specified actor entering");
            c.State.Signals.RemoveAll(s=>s.RuleId==rule.Id);c.PollTriggers();HotTestAudit.Check(!c.State.Signals.Any(s=>s.RuleId==rule.Id),"staying inside does not repeat entry");
            rule.Position.X+=20;c.PollTriggers();HotTestAudit.Check(c.State.Signals.Any(s=>s.RuleId==rule.Id&&s.Kind=="RangeLeave"),"moving detection center produces leaving event");
            c.State.Signals.RemoveAll(s=>s.RuleId==rule.Id);rule.RangeShape="Circle";rule.Radius=2;rule.Position.X-=20;rule.Detect="Hostile";c.State.Triggers.Remove(rule.Id);c.PollTriggers();HotTestAudit.Check(!c.State.Signals.Any(s=>s.RuleId==rule.Id&&s.Source=="hero"),"hostile detection excludes player actor");
            rule.Detect="Specific";c.State.Triggers.Remove(rule.Id);c.PollTriggers();HotTestAudit.Check(c.State.Signals.Any(s=>s.RuleId==rule.Id&&s.Kind=="RangeEnter"),"circle detects actor without stealth module");
            c.State.Signals.RemoveAll(s=>s.RuleId==rule.Id);c.State.Triggers.Remove(rule.Id);c.MapSpec.Rules[c.MapSpec.Rules.FindIndex(r=>r.Id==rule.Id)]=snapshot;
        }
    }
}
