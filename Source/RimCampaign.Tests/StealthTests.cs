using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;

static class StealthTests
{
    sealed class Host:IHost {public double Read(string key)=>0;public bool Execute(Act a,string source,double dt)=>true;}
    public static void Run(Action<bool,string> check)
    {
        Cell C(int x,int z)=>new Cell{X=x,Z=z};
        check(BookText.Normalize("段一\r\n\r\n段二")==BookText.Normalize("段一\n\n段二"),"book identity tolerates native XML newline conversion");
        check(BookText.Normalize(" 内容 \n")!="内容"&&BookText.Normalize("内容 A")!=BookText.Normalize("内容 B"),"book identity normalization preserves meaningful text and spacing");
        foreach(var origin in new[]{C(0,0),C(10,20),C(-10,20),C(10,-20)}){
            var expected=new[]{(-1,-1),(0,-1),(1,-1),(-1,0),(1,0),(-1,1),(0,1),(1,1),(0,2),(2,0),(0,-2),(-2,0)};
            check(Enumerable.Range(-3,7).All(x=>Enumerable.Range(-3,7).All(z=>
                StealthMath.InTakedownReach(origin,C(origin.X+x,origin.Z+z))==expected.Contains((x,z)))),
                "takedown footprint is exactly the twelve-cell four-way union at "+origin.X+","+origin.Z);
        }
        check(StealthMath.InCone(C(5,5),C(5,10),0,5,90),"north cone includes range edge");
        check(!StealthMath.InCone(C(5,5),C(5,11),0,5,90)&&!StealthMath.InCone(C(5,5),C(5,4),0,5,90),"cone excludes distant and rear targets");
        check(StealthMath.InCone(C(5,5),C(10,5),1,8,90)&&StealthMath.InCone(C(5,5),C(5,0),2,8,90)&&StealthMath.InCone(C(5,5),C(0,5),3,8,90),"east south west rotation mapping");
        var g=new GuardSpec{DetectionTicks=100,SearchTicks=300};var memory=new GuardMemory();
        var near=new GuardMemory();var far=new GuardMemory();near.Observe(C(1,0),0,20,g,false,1);far.Observe(C(14,0),0,20,g,false,14);
        check(near.Suspicion>far.Suspicion*4&&far.Suspicion>0,"distant target accumulates suspicion more slowly");
        far.Observe(C(14,0),100,1,g,true,14);far.LoseSight(200,10,g);
        check(far.Suspicion==1&&far.FullSuspicionUntil==280,"full suspicion holds after sight is lost");
        far=Json.Copy(far);far.LoseSight(281,10,g);check(far.Suspicion<1,"saved full-suspicion deadline allows decay only after hold");
        check(Json.Read<GuardSpec>("{}").FullSuspicionHoldTicks==180&&Math.Abs(Json.Read<GuardSpec>("{}").FarDetectionMultiplier-.2)<.001,"legacy guards receive distance and hold defaults");
        check(Json.Read<PatrolPoint>("{}").Checkpoint,"patrol points default to checkpoint dwell");
        var position=C(8,8);memory.Observe(position,10,20,g);position.X=30;
        check(memory.LastKnown.X==8&&!memory.Identified&&memory.Mode=="Observing","white observation snapshots a position before identification");
        memory.LoseSight(20,10,g);check(memory.Mode=="Patrol"&&memory.LastKnown.X==8,"white-stage LOS loss returns to patrol");
        memory.Observe(C(9,8),30,100,g);check(memory.Identified&&memory.Mode=="Combat","sustained sight identifies actor");
        memory.LoseSight(40,10,g);check(memory.Mode=="Search"&&memory.LastKnown.X==9,"combat loses its live target on LOS loss");
        var reacquired=Json.Copy(memory);reacquired.Suspicion=.5;reacquired.Observe(C(9,8),41,1,g);
        check(reacquired.Mode=="Combat"&&reacquired.Suspicion==1,"known intruder immediately re-enters combat instead of repeating suspicion buildup");
        var receiver=new GuardMemory();receiver.Receive(memory,45,g);memory.LastKnown.X=33;
        var yellow=new GuardMemory();yellow.Observe(C(8,8),10,50,g);
        check(yellow.Suspicion==.5&&yellow.Mode=="Suspicious"&&!yellow.Identified&&!yellow.Alarmed,"half detection starts yellow investigation without combat");
        yellow.LoseSight(20,6,g);check(yellow.Mode=="Investigate","yellow-stage LOS loss searches the remembered location");
        check(receiver.LastKnown.X==9&&!receiver.Identified&&!receiver.Alarmed&&receiver.Suspicion==.5&&receiver.Mode=="Investigate","radio delivers yellow investigation without granting identification");
        var old=new GuardMemory{Identified=true,LastSeenTick=1,LastKnown=C(1,1)};receiver.Receive(old,50,g);
        receiver.Observe(C(9,8),46,1,g);
        check(receiver.Suspicion<1&&!receiver.Identified&&receiver.Mode=="Suspicious","reported suspect still requires personal identification");
        check(receiver.LastKnown.X==9,"stale radio cannot overwrite newer knowledge");
        var hearer=new GuardMemory();hearer.Hear(C(4,4),60,g,true);
        check(hearer.Alarmed&&!hearer.Identified&&hearer.LastKnown==null,"dangerous sounds do not identify actor or expose actor position");
        var saved=Json.Copy(receiver);check(saved.LastKnown.X==9&&!saved.Identified&&saved.SearchUntil==receiver.SearchUntil&&saved.Suspicion==receiver.Suspicion,"search memory survives serialization");
        var route=new PatrolRoute{PingPong=true,Points=new List<PatrolPoint>{new PatrolPoint(),new PatrolPoint(),new PatrolPoint()}};
        var patrol=new GuardMemory();var sequence=new List<int>();for(int i=0;i<6;i++){sequence.Add(patrol.RouteIndex);patrol.NextWaypoint(route);}
        check(sequence.SequenceEqual(new[]{0,1,2,1,0,1}),"pingpong patrol endpoints do not wrap");
        var map=new MapSpec();
        Rule Password(string id,string code)=>new Rule{Id=id,Password=code,Events=new List<EventSpec>{new EventSpec{Kind="Password"}},Actions=new List<Act>{new Act{Kind="Add",Target=id,Value=1}}};
        map.Rules.Add(Password("one","战-ORION-7"));map.Rules.Add(Password("two","战-ORION-7"));
        map.Rules.Add(new Rule{Id="wrong",Once=false,Events=new List<EventSpec>{new EventSpec{Kind="PasswordRejected",Target="one"}},Actions=new List<Act>{new Act{Kind="Add",Target="wrongCount",Value=1}}});
        var state=new RunState();var engine=new Engine(map,state,new Host());engine.Start();engine.Tick(0,0);
        check(!engine.SubmitPassword("one","战-orion-7"),"password is case sensitive by default");engine.Tick(0,0);
        check(!state.Firings.ContainsKey("one")&&!state.Firings.ContainsKey("two"),"wrong password cannot fire success actions");
        check(state.Variables["wrongCount"]==1,"wrong password can drive a separate rejection trigger");
        engine.Emit("Password");engine.Tick(0,0);check(!state.Firings.ContainsKey("one")&&!state.Firings.ContainsKey("two"),"unscoped password event cannot unlock triggers");
        check(engine.SubmitPassword("one","战-ORION-7")&&!engine.SubmitPassword("one","战-ORION-7"),"queued once-only password is deduplicated");
        state=Json.Copy(state);engine=new Engine(map,state,new Host());engine.Tick(0,0);
        check(state.Variables["one"]==1&&!state.Variables.ContainsKey("two"),"password matches only named trigger, including after save/load");
        check(!engine.SubmitPassword("one","战-ORION-7"),"completed once-only password cannot replay");
        map.Rules[1].PasswordCaseSensitive=false;map.Rules[1].PasswordTrimInput=true;
        check(engine.SubmitPassword("two","  战-orion-7  "),"explicit trim and case-insensitivity settings");engine.Tick(0,0);
        var disabled=Password("disabled","x");disabled.Enabled=false;map.Rules.Add(disabled);check(!engine.SubmitPassword("disabled","x"),"disabled password rejects input");
        check(engine.SubmitPasswordDetailed("disabled","x")==PasswordSubmission.Disabled&&engine.SubmitPasswordDetailed("missing","x")==PasswordSubmission.MissingRule,"password rejection distinguishes disabled and missing trigger");
        var numeric=Password("numeric","01440");numeric.PasswordTrimInput=true;map.Rules.Add(numeric);
        check(engine.SubmitPasswordDetailed("numeric","1440")==PasswordSubmission.Mismatch,"numeric password preserves significant leading zero");
        check(engine.SubmitPasswordDetailed("numeric"," ０１４４０ ")==PasswordSubmission.Accepted,"numeric password accepts full-width input and configured whitespace trimming");
        var legacy=Json.Read<MapSpec>("{}");check(legacy.Stealth!=null&&!legacy.Stealth.Enabled,"old maps remain opted out");
        check(Json.Read<Rule>("{}").PasswordCaseSensitive,"legacy trigger password defaults");
        var example=StealthFixture.Create();var errors=Validation.Check(Json.Copy(example));
        check(example.Maps[0].Stealth.Devices.All(d=>!d.AutoReport)&&example.Maps[0].Stealth.Devices.Any(d=>d.Kind=="SupplyCrate"),"sample cameras require living reporters and supplies use a crate");
        check(example.Maps[0].Objects.Where(p=>p.Id.EndsWith("-host")).All(p=>DeviceSpec.KindForDef(p.Def.Name)!=null),"sample devices are selectable purpose-specific building defs");
        check(errors.Count==0,"example validation: "+string.Join(";",errors));
        var camera=example.Maps[0].Stealth.Devices.First(d=>d.Kind=="Camera");
        camera.DetectionTicks=0;check(Validation.Check(example).Any(i=>i.Message.Contains("Camera detection time")),"camera requires positive reaction time");camera.DetectionTicks=180;
        var sound=example.Maps[0].Rules.SelectMany(r=>r.Actions).Single(a=>a.Kind=="SoundPulse");
        sound.SoundPulse.Sound.Package="test.audio";
        var dependencies=Dependencies.Scan(example,new[]{new Dependency{PackageId="rimcampaign.framework"},new Dependency{PackageId="test.audio"}});
        check(dependencies.Direct.Any(d=>d.PackageId=="test.audio"),"sound pulse audio dependency is exported");
        sound.SoundPulse.Targets.Add("nonexistent");check(Validation.Check(example).Any(i=>i.Message.Contains("sound recipient")),"invalid sound target is rejected");sound.SoundPulse.Targets.Remove("nonexistent");
        sound.SoundPulse.Radius=float.NaN;check(Validation.Check(example).Any(i=>i.Message.Contains("sound pulse")),"invalid sound radius is rejected");sound.SoundPulse.Radius=18;
        example.Maps[0].Stealth.Guards[0].Route="missing";check(Validation.Check(example).Any(i=>i.Message.Contains("patrol route")),"validation catches broken patrol binding");
        var failureMap=new MapSpec{Entry=new List<Act>{new Act{Kind="Fail",Text="任务失败：梅森暴露了。"}}};
        var fail=new RunState();var failEngine=new Engine(failureMap,fail,new Host());failEngine.Start();failEngine.Tick(1,1);failEngine.Tick(0,0);
        check(fail.Phase=="Defeated"&&Json.Copy(fail).FailureReason=="任务失败：梅森暴露了。","custom failure reason survives save/load");
    }
}
