using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;

static class CinematicTests {
    sealed class Host:IHost {
        public List<string> Calls=new List<string>();
        public double Read(string key)=>0;
        public bool Execute(Act a,string source,double delta){Calls.Add(a.Kind);return true;}
    }
    public static void Run(Action<bool,string> check){
        var scene=new Act{Kind="Cinematic",Steps=new List<Act>{new Act{Kind="Wait",Value=2,Freeze=true},new Act{Kind="Warp"},new Act{Kind="Move"},new Act{Kind="Communication",Text="Mason"},new Act{Kind="Communication",Text="Price"}}};
        var map=new MapSpec{Entry=new List<Act>{scene}};var state=new RunState();var host=new Host();var engine=new Engine(map,state,host);engine.Start();engine.Tick(0,1);
        check(host.Calls.SequenceEqual(new[]{"Cinematic"}),"cinematic begins once before its asynchronous children");
        check(state.Fibers.Single().Steps.Single().Kind=="Cinematic"&&state.Fibers.Single().Children.Count==1,"cinematic parent remains active during child wait");
        check(!state.Fibers.Single().Children.Single().Steps.Single(a=>a.Kind=="Warp").Freeze,"cinematic control lock does not freeze ticking warp and movement");
        engine.Tick(0,1);
        check(host.Calls.SequenceEqual(new[]{"Cinematic","Warp","Move","Communication","Communication"}),"cinematic preserves warp walk and two-line dialogue order");
        check(state.Fibers.Count==0,"cinematic completes only after every child");
        var copy=Json.Copy(map);check(copy.Entry[0].Kind=="Cinematic"&&copy.Entry[0].Steps.Count==5,"cinematic survives authoring serialization");
    }
}
