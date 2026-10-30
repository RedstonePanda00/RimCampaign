using System;
using System.Collections.Generic;
using RimCampaign.Core;

static class BossComponentTests
{
    public static void Run(Action<bool,string> check)
    {
        foreach(var n in new[]{0d,1,50,99,100})check(BossAdaptation.Compress(n)==n,"adaptation preserves low damage "+n);
        double previous=100;
        foreach(var n in new[]{100.001,110,200,500,1000,4000,500000,double.MaxValue}){
            double value=BossAdaptation.Compress(n);
            check(value>=previous&&value<=n&&value<=500,"adaptation monotone / never amplifies / bounded "+n);previous=value;
        }
        check(Math.Abs((BossAdaptation.Compress(100.001)-100)/.001-1)<.0001,"adaptation has continuous derivative at threshold");
        check(BossAdaptation.Compress(double.PositiveInfinity)==500,"infinite attack has finite adapted limit");
        check(BossAdaptation.Compress(double.NaN)==0,"NaN cannot poison boss HP");
        bool rejected=false;try{BossAdaptation.Compress(100,500,100);}catch(ArgumentException){rejected=true;}
        check(rejected,"invalid adaptation configuration rejected");
        var old=Json.Read<BossTemplate>("{\"MaxHealth\":3000}");
        check(old.Components!=null&&old.Components.Count==0,"old encounters remain without components");
        var template=new BossTemplate{Components=new List<BossComponentSpec>{new BossComponentSpec{Module="boss.adaptation",Parameters=new Dictionary<string,string>{{"threshold","77"}},DamageExceptions=new List<DefRef>{new DefRef{Kind="DamageDef",Name="CustomShip"}}}}};
        var copy=Json.Copy(template);
        check(copy.Components[0].Parameters["threshold"]=="77"&&copy.Components[0].DamageExceptions[0].Name=="CustomShip"&&copy.Components[0].Enabled,"component parameters, exceptions and enabled flag survive JSON");
        check(Json.Read<BossComponentSpec>("{\"Module\":\"plugin.example\"}").Enabled,"minimal plugin specs default enabled");
        var state=Json.Copy(new Dictionary<string,Dictionary<string,string>>{{"component:retaliation",new Dictionary<string,string>{{"charge","123"}}}});
        check(state.ContainsKey("component:retaliation")&&state["component:retaliation"]["charge"]=="123","component state dictionary preserves colon keys and numeric strings");
    }
}
