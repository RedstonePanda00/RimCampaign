using System.Collections.Generic;

namespace RimCampaign.Core
{
    public static class StealthFixture
    {
        public static Project Create()
        {
            Cell C(int x,int z)=>new Cell{X=x,Z=z};
            var p=new Project{Id="stealth-example",Name="RimCampaign 潜行验证",PackageId="rimcampaign.stealth.example",Author="RimCampaign"};
            var profile=new Profile{Id="operative",Name="测试行动员",GenerateFrom=new DefRef{Kind="PawnKindDef",Name="Colonist"}};
            p.Profiles.Add(profile);
            var m=new MapSpec{Id="stealth-lab",Name="潜行训练场",Width=40,Height=30};p.Maps.Add(m);
            m.Objects.Add(new Placement{Id="start",Name="行动员起点",Kind="Pawn",Role="hero",Position=C(5,5)});
            void Wall(int x,int z)=>m.Objects.Add(new Placement{Id="wall-"+x+"-"+z,Name="隔墙",Def=new DefRef{Name="Wall"},Stuff=new DefRef{Name="BlocksGranite"},Position=C(x,z),Faction="Hostile"});
            for(int z=2;z<26;z++)if(z<8||z>10)Wall(13,z);
            for(int x=14;x<34;x++)if(x<23||x>25)Wall(x,17);
            m.Objects.Add(new Placement{Id="weapon",Name="可选武器",Def=new DefRef{Name="Gun_Autopistol"},Position=C(6,5)});
            foreach(var i in new[]{0,1})m.Objects.Add(new Placement{Id="guard-"+i,Name="巡逻守卫 "+(i+1),Kind="PawnKind",Def=new DefRef{Kind="PawnKindDef",Name="Mercenary_Gunner"},Faction="Hostile",Position=i==0?C(20,8):C(28,22),Rotation=1});
            m.Components.Add(new MapComponentSpec{Type="RimCampaign.Runtime.StealthMapComponent"});
            var s=m.Stealth;s.Enabled=true;s.PlayerSight=12;
            s.Routes.Add(new PatrolRoute{Id="route-a",Name="南区往返",PingPong=true,Points=new List<PatrolPoint>{new PatrolPoint{Position=C(20,8),Facing=1,WaitTicks=180},new PatrolPoint{Position=C(30,8),Facing=0,WaitTicks=180}}});
            s.Routes.Add(new PatrolRoute{Id="route-b",Name="北区环线",Points=new List<PatrolPoint>{new PatrolPoint{Position=C(28,22),Facing=1,WaitTicks=180},new PatrolPoint{Position=C(34,22),Facing=2,WaitTicks=180},new PatrolPoint{Position=C(34,26),Facing=3,WaitTicks=180},new PatrolPoint{Position=C(28,26),Facing=2,WaitTicks=180}}});
            for(int i=0;i<2;i++)s.Guards.Add(new GuardSpec{Id="watch-"+i,Name="守卫控制 "+(i+1),Pawn="guard-"+i,Route=i==0?"route-a":"route-b",Uplink="uplink",SightRange=10,DetectionTicks=120,ReportTicks=300});
            // Standard selectable buildings own device commands; behavior remains map-authored.
            void Device(string id,string name,string kind,int x,int z){
                m.Objects.Add(new Placement{Id=id+"-host",Name=name,Def=new DefRef{Name=kind=="Interaction"||kind=="SupplyCrate"?"RC_SupplyCrate":kind=="Password"?"RC_Terminal":"RC_"+kind,Package="rimcampaign.framework"},Faction="Hostile",Position=C(x,z)});
                s.Devices.Add(new DeviceSpec{Id=id,Name=name,Kind=kind,Host=id+"-host",Position=C(x,z)});
            }
            Device("reward","补给箱：药品","SupplyCrate",8,11);m.Objects.Find(o=>o.Id=="reward-host").Contents.Add(new DeviceLoot{Def=new DefRef{Name="MedicineIndustrial"},Count=2});
            Device("broadcast","可入侵广播","Broadcast",10,8);
            Device("camera","走廊摄像头","Camera",16,13);
            s.Devices[2].Facing=1;s.Devices[2].SightRange=10;s.Devices[2].Uplink="uplink";s.Devices[2].ReportTicks=480;
            Device("uplink","对外通讯终端","Uplink",30,14);
            Device("password","密码终端（ORION-7）","Password",35,25);s.Devices[4].PasswordRule="access-code";
            m.Regions.Add(new Region{Id="exit",Name="取得通行许可后撤离",Min=C(36,25),Max=C(38,28)});
            m.Objectives.Add(new Objective{Id="alive",Name="行动员必须存活",Mode="Survive",Role="hero"});
            m.Objectives.Add(new Objective{Id="access",Name="输入密码取得通行许可"});
            m.Hud.Add(new HudWidget{Id="goals",Kind="Objectives",Name="目标",Height=80,Y=90});
            Rule Rule(string id,string name,string kind,string target,params Act[] actions){var r=new Rule{Id=id,Name=name,Position=C(3,26-m.Rules.Count*2),Events=new List<EventSpec>{new EventSpec{Kind=kind,Target=target}},Actions=new List<Act>(actions)};m.Rules.Add(r);return r;}
            Rule("reward-trigger","领取额外装备","InteractionCompleted","reward",new Act{Kind="Communication",Text="补给已取出。",Communication=new CommunicationSpec{Speaker="$source",Duration=5}});
            var audio=Rule("audio-trigger","广播声音范围触发示例","InteractionCompleted","broadcast",new Act{Kind="SoundPulse",Target="broadcast-host",Text="training-broadcast",SoundPulse=new SoundPulseSpec{Sound=new DefRef{Kind="SoundDef",Name="Click"},Radius=18,Receivers="Guards",Targets=new List<string>{"guard-0"}},Steps=new List<Act>{new Act{Kind="Add",Target="soundResponses",Value=1}}});audio.Once=false;
            var password=Rule("access-code","输入指定字符","Password","",new Act{Kind="Set",Target="access",Value=1},new Act{Kind="Objective",Target="access",Text="Complete"});password.Password="ORION-7";
            var exit=Rule("leave","携带通行许可撤离","Enter","exit",new Act{Kind="Win"});exit.When=new Condition{Op="AtLeast",Key="access",Value=1};
            Rule("leak","情报传出即失败","IntelLeaked","",new Act{Kind="Fail",Text="任务失败：行动员暴露了。"});
            m.Entry.Add(new Act{Kind="Communication",Text="通讯测试：选择建筑操作后，再点选执行角色。摄像头与密码也可以触发后续通讯。",Communication=new CommunicationSpec{Speaker="hero",SpeakerName="行动员",Duration=7}});
            m.Entry.Add(new Act{Kind="Message",Text="测试场：选中建筑使用其操作按钮；选中行动员使用制伏及自动近战开关。坐标：补给(8,11)，广播(10,8)，摄像头(16,13)，通讯(30,14)，密码(35,25)。密码 ORION-7。警觉不等于泄密；阻止对外报告。"});
            var c=new Campaign{Id="stealth-training",Name=p.Name,Start="training"};p.Campaigns.Add(c);
            c.Nodes.Add(new Node{Id="training",Name=m.Name,Map=m.Id,Roles=new Dictionary<string,string>{{"hero",profile.Id}},Hunger=false,Mood=false,Work=false,Construction=false,Research=false,Storyteller=false});
            return p;
        }
    }
}
