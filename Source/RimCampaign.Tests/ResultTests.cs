using System;
using RimCampaign.Core;

static class ResultTests
{
    public static void Run(Action<bool,string> check)
    {
        var spec=new ResultSpec();var p=new MissionPerformance();
        check(p.Score(spec)==100&&p.Rank(spec)=="S","unseen nonlethal extraction earns S");
        p.Record("subdue","guard1");p.Record("subdue","guard1");
        check(p.SurvivingSubdued==1&&p.Score(spec)==100,"repeated takedown cannot farm a score bonus");
        p.Record("kill","guard1");p.Record("kill","guard1");
        check(p.Killed.Count==1&&p.SurvivingSubdued==0&&p.Score(spec)==92,"subdued then killed is one death, not a nonlethal result");
        p.Record("detected","guard1");p.Record("detected","guard1");p.Record("report","guard1");
        check(p.Score(spec)==67&&p.Rank(spec)=="B","independent exposure report and death penalties combine");
        p=Json.Copy(p);p.Record("report","guard1");p.Record("detected","guard1");
        check(p.Reporters.Count==1&&p.DetectedBy.Count==1&&p.Score(spec)==67,"same-version load preserves deduplication and score");
        for(int i=0;i<20;i++)p.Record("kill","other"+i);
        check(p.Score(spec)==0&&p.Rank(spec)=="D","score has a zero floor");
        var result=new MissionResult{Node="one",Seconds=25,Performance=Json.Copy(p)};
        p.Record("report","another");
        check(result.Performance.Reporters.Count==1,"result snapshot is independent of later live counters");
    }
}
