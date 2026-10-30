using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
static class TaskTreeTests {
 sealed class Host:IHost,ITaskActorHost {
  public bool Alive=true,Finished;public List<string> Seen=new List<string>();
  public double Read(string key)=>0;
  public bool TaskActorAvailable(string actor)=>Alive;
  public bool Execute(Act a,string source,double delta){if(a.Kind=="walk"&&!Finished)return false;Seen.Add(a.Kind+":"+source);return true;}
 }
 public static void Run(Action<bool,string> check){
  var handle=new Rule{Id="origin",ShowOnMap=true,Position=new Cell{X=12,Z=34},Events=new List<EventSpec>{new EventSpec{Kind="Periodic",Seconds=30}},Actions=new List<Act>{new Act{Kind="rc.reinforce",Origin="origin"}}};
  check(Json.Copy(new Act{Kind="rc.reinforce",Arrival="Pods"}).Arrival=="Pods"&&new Act().Arrival=="Ground","arrival mode survives serialization and ground remains default");
  var invalidProject=Demo.Create();var invalidMap=invalidProject.Maps[0];invalidMap.Entry.Add(new Act{Kind="rc.reinforce",Arrival="Unknown"});
  check(Validation.Check(invalidProject).Any(e=>e.Message.Contains("arrival mode")),"invalid arrival mode rejected");
  invalidMap.Entry.Add(new Act{Kind="rc.buildOutpost",Target="missing",Text="missing",Origin="missing"});
  check(Validation.Check(invalidProject).Any(e=>e.Message.Contains("Outpost needs")),"missing outpost engineer, blueprint and origin bindings rejected");
  var restored=Json.Copy(handle);check(TriggerGeometry.Visible(restored)&&!TriggerGeometry.Spatial(restored)&&restored.Actions[0].Origin=="origin","periodic map handle and spawn origin survive serialization without a spatial condition");
  var duplicate=TriggerGeometry.Copy(handle,2,3);check(duplicate.Actions[0].Origin==duplicate.Id&&duplicate.Id!=handle.Id&&duplicate.Position.X==14&&duplicate.Position.Z==37,"copying periodic handle remaps self-referenced spawn origin");
  var host=new Host();var tree=new ActionGroup{Id="tasks",Actor="$source",Actions=new List<Act>{new Act{Kind="WaitUntil",When=new Condition{Op="AtLeast",Key="gate",Value=1}},new Act{Kind="walk"},new Act{Kind="operate"}}};
  var map=new MapSpec{Groups=new List<ActionGroup>{tree}};var state=new RunState();var engine=new Engine(map,state,host);engine.Start();engine.Enqueue(new List<Act>{new Act{Kind="Group",Target="tasks"}},source:"soldier-a");engine.Tick(1,1);
  check(host.Seen.Count==0,"task condition gates movement");state.Variables["gate"]=1;engine.Tick(1,1);check(host.Seen.Count==0,"unfinished movement gates operation");
  state=Json.Copy(state);engine=new Engine(map,state,host);host.Finished=true;engine.Tick(1,1);
  check(host.Seen.SequenceEqual(new[]{"walk:soldier-a","operate:soldier-a"}),"bound actor and task cursor survive save/load");
  host.Seen.Clear();tree.Actor="hero";engine.Enqueue(new List<Act>{new Act{Kind="Group",Target="tasks"}},source:"soldier-b");engine.Tick(1,1);check(host.Seen.All(s=>s.EndsWith(":hero")),"explicit task actor overrides event source");
  host.Seen.Clear();host.Finished=false;engine.Enqueue(new List<Act>{new Act{Kind="Group",Target="tasks"}});engine.Tick(1,1);host.Alive=false;host.Finished=true;engine.Tick(1,1);check(host.Seen.Count==0&&state.Fibers.Count==0,"dead actor cancels pending tasks without running successors");
  host=new Host();map=new MapSpec{Rules=new List<Rule>{new Rule{Id="waves",Once=false,Events=new List<EventSpec>{new EventSpec{Kind="Periodic",Seconds=5}},When=new Condition{Op="AtLeast",Key="enabled",Value=1},Actions=new List<Act>{new Act{Kind="wave"}}}}};state=new RunState();engine=new Engine(map,state,host);engine.Start();engine.Tick(5,5);check(host.Seen.Count==0,"periodic condition prevents arrivals");state.Variables["enabled"]=1;engine.Tick(0,100);check(host.Seen.Count==0,"periodic timer pauses with game time");engine.Tick(5,5);check(host.Seen.Count==1,"periodic trigger produces one wave per interval");state=Json.Copy(state);engine=new Engine(map,state,host);engine.Tick(0,0);check(host.Seen.Count==1,"periodic boundary does not replay after loading");engine.Tick(20,20);check(host.Seen.Count==2,"large time delta does not burst overdue waves");
 }
}
