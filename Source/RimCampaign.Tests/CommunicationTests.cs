using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;

static class CommunicationTests
{
    sealed class Host:IHost,IConversationHost {
        public CommunicationQueue Queue=new CommunicationQueue();
        public double Read(string key)=>0;
        public bool Execute(Act a,string source,double delta)=>Queue.Execute(a,source,delta);
        public bool ExecuteConversation(List<Act> lines,string source,double delta)=>Queue.ExecuteConversation(lines,source,delta);
    }
    public static void Run(Action<bool,string> check) {
        Act Message(string name)=>new Act{Kind="Communication",Text=name,Communication=new CommunicationSpec{Duration=2,Speaker="$source"}};
        var first=Message("first");var second=Message("second");var queue=new CommunicationQueue();
        check(!queue.Execute(first,"hero",0)&&queue.Current.Source=="hero","communication automatically starts with triggering source");
        queue.Execute(second,"ally",100);queue.Execute(first,"hero",.75);
        check(queue.Messages.Count==2&&queue.Messages[1].Elapsed==0,"queued parallel communication does not age before its turn");
        queue=Json.Copy(queue);
        check(queue.Current.Elapsed==.75&&queue.Current.Spec.Duration==2&&queue.Messages[1].Source=="ally","communications save queue, elapsed time and speaker source");
        check(!queue.Execute(first,"hero",0)&&queue.Current.Elapsed==.75,"paused communications do not advance");
        check(queue.Execute(first,"hero",1.25)&&queue.Current.Text=="second","communication auto ends then shows next queued message");
        queue.Retain(new[]{first.Id});check(queue.Current==null,"cancelled actions cannot leave a stale communication blocking the queue");
        var a=new List<Act>{Message("A1"),Message("A2")};var b=new List<Act>{Message("B1"),Message("B2")};
        var cues=new CommunicationQueue();cues.ExecuteConversation(a,"hero",0);
        check(cues.TryNotifyConversationStart()&&!cues.TryNotifyConversationStart(),"conversation start cue occurs once across repeated presentation calls");
        cues.ExecuteConversation(a,"hero",2);check(!cues.TryNotifyConversationStart(),"second line does not replay conversation start cue");
        cues=Json.Copy(cues);check(!cues.TryNotifyConversationStart(),"save/load retains already played conversation cue");
        cues.ExecuteConversation(b,"ally",0);check(cues.TryNotifyConversationStart(),"interrupting conversation has its own start cue");
        cues.ExecuteConversation(b,"ally",2);cues.ExecuteConversation(b,"ally",2);
        check(cues.Current.Text=="A1"&&cues.TryNotifyConversationStart()&&!cues.TryNotifyConversationStart(),"restarted conversation plays one fresh start cue");
        var patrol=new ConversationMemory();check(patrol.TryNotifyStart()&&!patrol.TryNotifyStart()&&!Json.Copy(patrol).TryNotifyStart(),"patrol start cue is deduplicated and saved");
        queue=new CommunicationQueue();queue.ExecuteConversation(a,"hero",0);queue.ExecuteConversation(a,"hero",2);queue.ExecuteConversation(a,"hero",.75);
        check(queue.Current.Text=="A2"&&queue.Current.Elapsed==.75,"conversation advances into its second line");
        queue.ExecuteConversation(b,"ally",0);
        check(queue.Current.Text=="B1"&&queue.Messages.Where(m=>m.ConversationId==a[0].Id).All(m=>!m.Completed&&m.Elapsed==0),"new conversation interrupts and resets the whole previous dialogue");
        queue.ExecuteConversation(a,"hero",100);queue.Retain(new[]{a[0].Id,b[0].Id});
        check(queue.Messages.Count==4&&queue.Messages.Where(m=>m.ConversationId==a[0].Id).All(m=>m.Elapsed==0),"suspended block stays at its beginning and retains all lines");
        queue=Json.Copy(queue);queue.ExecuteConversation(b,"ally",2);
        check(queue.Current.Text=="B2","interrupting conversation remains contiguous after serialization");
        check(queue.ExecuteConversation(b,"ally",2)&&queue.Current.Text=="A1"&&queue.Current.Elapsed==0,"B finishes before A restarts from its first line");
        queue.ExecuteConversation(a,"hero",2);check(queue.Current.Text=="A2","restarted A plays its full sequence");
        check(queue.ExecuteConversation(a,"hero",2)&&queue.Current==null,"completed dialogue block leaves no queued remnants");
        var groupedHost=new Host();var groupedState=new RunState();var groupedEngine=new Engine(new MapSpec(),groupedState,groupedHost);
        groupedEngine.Enqueue(new List<Act>{new Act{Kind="Add",Target="reward",Value=1},Message("A1"),new Act{Kind="If",Steps=new List<Act>{Message("A2")}},new Act{Kind="Add",Target="done",Value=1}});
        groupedEngine.Tick(0,0);groupedEngine.Tick(2,2);groupedEngine.Tick(.5,.5);
        groupedEngine.Enqueue(new List<Act>{Message("B")});groupedEngine.Tick(0,0);groupedEngine.Tick(2,2);
        check(groupedHost.Queue.Current.Text=="A1"&&groupedState.Variables["reward"]==1&&!groupedState.Variables.ContainsKey("done"),"engine replays interrupted dialogue without replaying earlier reward effects");
        groupedEngine.Tick(2,2);groupedEngine.Tick(2,2);
        check(groupedState.Variables["reward"]==1&&groupedState.Variables["done"]==1&&groupedState.Fibers.Count==0,"story effects around a restarted conversation execute exactly once");
        var defaults=Json.Read<CommunicationSpec>("{}");check(defaults.Duration==6&&defaults.CropWidth==1&&defaults.Width==620&&defaults.RendererData!=null&&!defaults.Errors().Any(),"old JSON gets safe communication defaults");
        check(!CommunicationSpec.ValidCrop(float.NaN,0,1,1)&&!CommunicationSpec.ValidCrop(0,0,1.01f,1)&&CommunicationSpec.ValidCrop(.25f,0,.5f,1),"portrait crop validation rejects invalid and nonfinite coordinates");
        defaults.Duration=double.PositiveInfinity;check(defaults.Errors().Any(),"invalid communication timing is rejected");
        var host=new Host();var map=new MapSpec();map.Entry.Add(first);map.Entry.Add(new Act{Kind="Set",Target="afterComms",Value=1});
        var state=new RunState();var engine=new Engine(map,state,host);engine.Start();engine.Tick(0,0);engine.Tick(1,1);
        var savedState=Json.Copy(state);host.Queue=Json.Copy(host.Queue);engine=new Engine(map,savedState,host);engine.Tick(1,1);engine.Tick(0,0);
        check(savedState.Variables.TryGetValue("afterComms",out var after)&&after==1&&host.Queue.Current==null,"automatic communication resumes through native engine state and advances its next story action");
        var project=StealthFixture.Create();var action=Message("dependency");action.Communication.Speaker="hero";
        action.Communication.Portrait=new DefRef{Kind="CommunicationPortraitDef",Name="CustomPortrait",Package="portrait.mod"};
        action.Communication.RendererPackage="shader.mod";project.Maps[0].Entry.Add(action);
        var report=Dependencies.Scan(project,new[]{new Dependency{PackageId="rimcampaign.framework"},new Dependency{PackageId="portrait.mod"},new Dependency{PackageId="shader.mod"}});
        check(report.Errors.Count==0&&report.Direct.Any(d=>d.PackageId=="portrait.mod")&&report.Direct.Any(d=>d.PackageId=="shader.mod"),"export includes portrait and renderer dependencies");
        check(!Validation.Check(project).Any(),"communication sample validates");action.Communication.Speaker="missing";
        check(Validation.Check(project).Any(i=>i.Message.Contains("speaker")),"invalid speaker binding is rejected before export");
        project.Maps[0].Objects.Find(o=>o.Id=="reward-host").Contents[0].Count=-1;
        check(Validation.Check(project).Any(i=>i.Message.Contains("building contents")),"negative crate loot is rejected");
    }
}
