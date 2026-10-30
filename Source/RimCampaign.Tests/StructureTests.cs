using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RimCampaign.Core;

static class StructureTests {
    public static void Run(Action<bool,string> check){
        var preset=new StructureSpec{Width=7,Height=9,Name="Patrol",Objects=new List<Placement>{new Placement{Id="guard",Kind="PawnKind",Def=new DefRef{Kind="PawnKindDef",Name="Guard",Package="example.units"},Position=new Cell{X=2,Z=3},Faction="Hostile"}},PatrolRoutes=new List<PatrolRoute>{new PatrolRoute{Id="route",Points=new List<PatrolPoint>{new PatrolPoint{Position=new Cell{X=4,Z=5}}}}},Patrols=new List<CombatPatrolSpec>{new CombatPatrolSpec{Pawn="guard",Route="route"}}};
        Structures.Validate(preset);var copy=Json.Copy(preset);
        check(copy.Patrols.Single().Route==copy.PatrolRoutes.Single().Id&&copy.Objects.Single().Faction=="Hostile","preset preserves patrol and faction references");
        check(Structures.References(preset).Single().Package=="example.units","preset dependencies include the unit provider");
        copy.Patrols[0].Pawn="missing";bool rejected=false;try{Structures.Validate(copy);}catch(InvalidDataException){rejected=true;}check(rejected,"dangling preset patrol binding rejected");
        var map=new MapSpec{Base="PocketMission",Roles=new List<string>(),Name="Pocket mission"};map.Entry.Add(new Act{Kind="Set",Target="entered",Value=1});
        map.Rules.Add(new Rule{Events=new List<EventSpec>{new EventSpec{Kind="Signal",Target="complete"}},Actions=new List<Act>{new Act{Kind="Win"}}});
        var node=new Node{Map=map.Id};var campaign=new Campaign{Start=node.Id,Nodes=new List<Node>{node}};
        var mission=new MissionMapSpec{Project=new Project{Maps=new List<MapSpec>{map},Campaigns=new List<Campaign>{campaign}}};
        MissionMaps.Validate(mission);var restored=Json.Copy(mission);
        check(restored.Project.Maps.Single().Rules.Single().Actions.Single().Kind=="Win","mission format preserves completion logic");
        check(restored.Project.Maps.Single().TerrainInitialization==null,"mission format carries no source biome initialization");
        string folder=Path.Combine(Path.Combine(Directory.GetCurrentDirectory(),"artifacts"),"rc-structure-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var file=Path.Combine(folder,"mission.rcmission.json");MissionMaps.Export(file,mission);
        var defs=XDocument.Load(Path.ChangeExtension(file,"xml"));check(defs.Root.Element("RimCampaign.Runtime.MissionMapDef")!=null&&defs.Root.Element("SitePartDef")!=null,"mission export contains native-reference definitions and map size");
        check(Json.Load<MissionMapSpec>(file).Project.Maps[0].Rules.Count==1,"mission file round trip preserves rules");
        foreach(var f in Directory.GetFiles(folder))File.Delete(f);Directory.Delete(folder);
    }
}
