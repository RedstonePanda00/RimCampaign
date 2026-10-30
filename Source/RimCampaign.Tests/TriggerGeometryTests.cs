using System;
using System.Collections.Generic;
using RimCampaign.Core;

static class TriggerGeometryTests {
    public static void Run(Action<bool,string> check){
        var rule=new Rule{Id="source",Radius=5,Position=new Cell{X=10,Z=12},Events=new List<EventSpec>{new EventSpec{Kind="RangeEnter"}},Actions=new List<Act>{new Act{Kind="Enable",Target="source"}}};
        check(TriggerGeometry.Spatial(rule),"range event owns a spatial editor handle");
        check(TriggerGeometry.Contains(rule,10,12,13,16)&&!TriggerGeometry.Contains(rule,10,12,14,16),"circular boundary uses squared distance");
        rule.RangeShape="Rectangle";rule.RangeWidth=6;rule.RangeHeight=4;
        check(TriggerGeometry.Contains(rule,10,12,13,14)&&!TriggerGeometry.Contains(rule,10,12,14,14),"rectangular dimensions centered on trigger");
        check(TriggerGeometry.Contains(rule,20,25,23,27)&&!TriggerGeometry.Contains(rule,20,25,13,14),"moving center translates detection footprint");
        var copy=TriggerGeometry.Copy(rule,3,4);
        check(copy.Id!=rule.Id&&copy.Position.X==13&&copy.Position.Z==16&&copy.Actions[0].Id!=rule.Actions[0].Id&&copy.Actions[0].Target==copy.Id,"copy has independent identities and self references");
        copy.Events[0].Kind="RangeLeave";check(rule.Events[0].Kind=="RangeEnter","copied events are independent");
        var loaded=Json.Copy(copy);check(loaded.RangeShape=="Rectangle"&&loaded.RangeWidth==6&&loaded.Events[0].Kind=="RangeLeave","trigger geometry survives serialization");
        rule.Events[0].Kind="Death";check(!TriggerGeometry.Spatial(rule),"nonspatial events have no editor map handle");
    }
}
