using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;

static class MapModuleTests {
    public static void Run(Action<bool,string> check){
        var fixture=StealthFixture.Create();fixture.Maps[0].Stealth.Enabled=false;fixture.Maps[0].Stealth.Devices.First(d=>d.Kind=="Camera").DetectionTicks=0;
        check(Validation.Check(fixture).Any(i=>i.Message.Contains("Camera detection time")),"explicit module configuration governs validation even when old payload switch is false");
        var map=new MapSpec();check(map.Components.Count==0,"new map has no optional modules");
        check(Json.Copy(map).Components.Count==0,"empty module configuration round-trips");
        map.Components.Add(new MapComponentSpec{Type="Example.CustomModule",Enabled=false,Settings=new Dictionary<string,string>{{"radius","12"}}});
        var copy=Json.Copy(map);check(copy.Components.Single().Type=="Example.CustomModule"&&!copy.Components.Single().Enabled&&copy.Components.Single().Settings["radius"]=="12","custom module type, settings and disabled state round-trip");
        var route=new PatrolRoute{Id="route",Points=new List<PatrolPoint>{new PatrolPoint{Marker="a"},new PatrolPoint{Marker="b"}}};
        map.PatrolRoutes.Add(route);map.Objects.AddRange(new[]{new Placement{Id="a",Kind="Marker",Position=new Cell{X=4,Z=5}},new Placement{Id="b",Kind="Marker",Position=new Cell{X=8,Z=9}}});
        PatrolAuthoring.Synchronize(map);check(route.Points[0].Position.X==4&&map.CombatPatrols.Count==0,"route is drawable before binding a host");
        var history=new History<MapSpec>(map);history.Edit(m=>{foreach(var marker in m.Objects){marker.Position.X+=3;marker.Position.Z+=2;marker.Rotation=1;}PatrolAuthoring.Synchronize(m);});
        check(history.Value.PatrolRoutes[0].Points[0].Position.X==7&&history.Value.PatrolRoutes[0].Points[1].Position.Z==11&&history.Value.PatrolRoutes[0].Points.All(p=>p.Facing==1),"batch waypoint movement and facing synchronize to route");
        history.Undo();check(history.Value.PatrolRoutes[0].Points[0].Position.X==4,"waypoint movement undo restores route coordinates");
        history.Redo();history.Edit(m=>{m.Objects.RemoveAll(o=>o.Id=="a");PatrolAuthoring.Synchronize(m);});check(history.Value.PatrolRoutes[0].Points.Count==1&&history.Value.PatrolRoutes[0].Points[0].Marker=="b","deleting a waypoint removes only its route point");
        history.Undo();check(history.Value.PatrolRoutes[0].Points.Count==2,"waypoint deletion is undoable");
        map.Rewards.Add(new MapRewardSpec{Id="book",KeepForExtraction=true,Contents=new List<DeviceLoot>{new DeviceLoot{Def=new DefRef{Name="TextBook"},Book=new BookText{Title="A",Text="B"}}}});
        map.Components.Clear();copy=Json.Copy(map);check(copy.Rewards.Single().Contents[0].Book.Text=="B"&&copy.Rewards[0].KeepForExtraction&&copy.Components.Count==0,"generation rewards do not require a logic module");
    }
}
