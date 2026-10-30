using System.Collections.Generic;
using RimCampaign.Core;

public static class Demo
{
    public static Project Create() {
        var hero=new Profile{Id="captain",Name="Captain"};
        hero.Payload=@"{""Kind"":""Colonist"",""First"":""Alex"",""Nick"":""Captain"",""Last"":""Chen"",""Gender"":1,""Age"":216000000,""Chronological"":216000000,""Childhood"":""ColonyChild59"",""Adulthood"":null,""Body"":""Male"",""Head"":""Male_AverageNormal"",""Hair"":""Shaved"",""HairColor"":[0.2,0.2,0.2,1],""SkinColor"":[0.7,0.5,0.4,1],""Beard"":null,""FaceTattoo"":null,""BodyTattoo"":null,""Xenotype"":null,""Traits"":[],""Skills"":[{""Def"":""Shooting"",""Level"":10,""Passion"":1,""Xp"":0}],""Genes"":[],""Health"":[],""Apparel"":[],""Equipment"":[],""Inventory"":[{""Def"":""MealSurvivalPack"",""Stuff"":null,""Count"":10,""HitPoints"":75,""Quality"":-1,""Color"":null}]}";
        foreach(var pair in new[]{new[]{"PawnKindDef","Colonist"},new[]{"BackstoryDef","ColonyChild59"},new[]{"BodyTypeDef","Male"},new[]{"HeadTypeDef","Male_AverageNormal"},new[]{"HairDef","Shaved"},new[]{"SkillDef","Shooting"},new[]{"ThingDef","MealSurvivalPack"}})
            hero.References.Add(new DefRef{Kind=pair[0],Name=pair[1]});
        var p=new Project{Id="demo",Name="RimCampaign 示例：撤离",PackageId="rimcampaign.demo",Author="RimCampaign",Profiles=new List<Profile>{hero}};
        MapSpec Map(string id,string name) {
            var m=new MapSpec{Id=id,Name=name,Width=50,Height=50};
            m.Objects.Add(new Placement{Id=id+"_hero",Name="英雄起点",Kind="Pawn",Role="hero",Position=new Cell{X=8,Z=25}});
            m.Objects.Add(new Placement{Id=id+"_exit",Name="出口镜头",Kind="Marker",Position=new Cell{X=38,Z=25}});
            m.Regions.Add(new Region{Id=id+"_zone",Name="撤离区域",Min=new Cell{X=35,Z=20},Max=new Cell{X=42,Z=30}});
            m.Objectives.Add(new Objective{Id=id+"_survive",Name="英雄必须存活",Mode="Survive",Role="hero",Required=true});
            m.Hud.Add(new HudWidget{Kind="Objectives",Name="任务目标",Height=100});
            m.Hud.Add(new HudWidget{Kind="Counter",Name="计时",Text="已用时间",Binding="elapsed",Y=210});
            m.Entry.Add(new Act{Kind="Fade",Text="in",Value=1,Freeze=true});
            m.Entry.Add(new Act{Kind="Subtitle",Text=name+"：抵达蓝色撤离区域。",Value=3,Freeze=true});
            m.Defeat.Add(new Act{Kind="Subtitle",Text="英雄阵亡。行动失败。",Value=2,Freeze=true});
            m.Defeat.Add(new Act{Kind="Fade",Text="out",Value=1,Freeze=true});
            m.Rules.Add(new Rule{Name="抵达出口",Events=new List<EventSpec>{new EventSpec{Kind="Enter",Target=id+"_zone"}},Actions=new List<Act>{new Act{Kind="Win"}}});
            p.Maps.Add(m);return m;
        }
        var facility=Map("facility","设施撤离");var rescued=Map("rescued","护送路线");var direct=Map("direct","快速撤离");var colony=Map("colony","开放殖民地");var finale=Map("finale","终章");
        colony.Entry.Clear();colony.Rules.Clear();
        facility.Entry.Add(new Act{Kind="Dialogue",Text="是否救援被困人员？",Freeze=true,Steps=new List<Act>{
            new Act{Name="实施救援",Steps=new List<Act>{new Act{Kind="CampaignSet",Target="rescued",Value=1}}},
            new Act{Name="直接撤离",Steps=new List<Act>{new Act{Kind="CampaignSet",Target="rescued",Value=0}}}
        }});
        finale.Rules.Clear();finale.Rules.Add(new Rule{Name="结束",Events=new List<EventSpec>{new EventSpec{Kind="Elapsed",Seconds=5}},Actions=new List<Act>{new Act{Kind="Win"}}});
        var c=new Campaign{Id="evacuation",Name=p.Name,Start="facility",Variables=new Dictionary<string,double>{{"rescued",0}}};
        foreach(var m in p.Maps)c.Nodes.Add(new Node{Id=m.Id,Name=m.Name,Map=m.Id,Kind=m==colony?"Open":"Story",Roles=new Dictionary<string,string>{{"hero",hero.Id}},X=80+c.Nodes.Count*200,Y=m==direct?260:100,Storyteller=m==colony});
        c.Edges.Add(new Edge{From="facility",To="rescued",Priority=10,When=new Condition{Op="Equal",Key="campaign.rescued",Value=1}});
        c.Edges.Add(new Edge{From="facility",To="direct"});
        c.Edges.Add(new Edge{From="rescued",To="colony"});c.Edges.Add(new Edge{From="direct",To="colony"});c.Edges.Add(new Edge{From="colony",To="finale"});
        p.Campaigns.Add(c);return p;
    }
}
