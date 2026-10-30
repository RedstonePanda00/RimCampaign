using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimCampaign.Runtime {
    // Explicit encounter behavior only. No normal pawn pathfinding or damage patches.
    [CampaignModule("boss.breachApproach","BossBehavior","沿直线接近，定时破坏指定障碍；参数 obstacleDefs、breachTicks、maxObstacles（整段直线障碍上限，默认3）。")]
    public sealed class BossBreachApproach:IBossBehaviorModule {
        static int Read(IDictionary<string,string> state,string key,int fallback=0)=>state.TryGetValue(key,out var text)&&int.TryParse(text,out var n)?n:fallback;
        public bool Execute(BossAction action,IBossContext context){
            var pawn=context.Pawn as Pawn;var target=context.Target as Pawn;var map=pawn?.Map;
            if(map==null||target?.Spawned!=true||target.Map!=map||pawn.Dead||pawn.Downed)return false;
            var owner=BossMapComponent.For(pawn);
            if(owner==null||!owner.Active||owner.FallenUntil>=0)return false;
            var c=CampaignController.ForMap(map);
            if(c?.Active!=true||c.Map!=map||c.Presentation||c.State.Phase!="Running")return false;
            var state=context.State;
            if(pawn.Position.DistanceTo(target.Position)<=action.Range&&GenSight.LineOfSight(pawn.Position,target.Position,map)){
                state.Remove("obstacle");state.Remove("elapsed");return false;
            }
            if(pawn.stances.FullBodyBusy)return false;
            var previousGoal=new IntVec3(Read(state,"goalX",-1),0,Read(state,"goalZ",-1));
            var origin=new IntVec3(Read(state,"originX",pawn.Position.x),0,Read(state,"originZ",pawn.Position.z));
            if(previousGoal!=target.Position){origin=pawn.Position;state["originX"]=origin.x.ToString();state["originZ"]=origin.z.ToString();state["goalX"]=target.Position.x.ToString();state["goalZ"]=target.Position.z.ToString();}
            var line=Line(origin,target.Position);var index=line.IndexOf(pawn.Position);
            if(index<0){line=Line(pawn.Position,target.Position);state["originX"]=pawn.Position.x.ToString();state["originZ"]=pawn.Position.z.ToString();index=0;}
            if(index+1>=line.Count)return false;
            var defs=action.Parameters.TryGetValue("obstacleDefs",out var names)?names.Split(','):new[]{"Wall","CollapsedRocks"};
            int limit=action.Parameters.TryGetValue("maxObstacles",out var maximum)&&int.TryParse(maximum,out var max)?Math.Max(0,max):3;
            if(TooManyObstacles(line,index,map,defs,limit)){
                // Count the entire remaining shortcut, not just the adjacent wall or a batch of three.
                state.Remove("obstacle");state.Remove("elapsed");state.Remove("lastTick");
                return context.Approach(action.Range);
            }
            var next=line[index+1];var obstacle=next.GetEdifice(map);
            // Native diagonal movement cannot clip through a blocked corner.
            if(next.x!=pawn.Position.x&&next.z!=pawn.Position.z){
                var a=new IntVec3(next.x,0,pawn.Position.z);var b=new IntVec3(pawn.Position.x,0,next.z);
                if(!a.Walkable(map)){next=a;obstacle=a.GetEdifice(map);}
                else if(!b.Walkable(map)){next=b;obstacle=b.GetEdifice(map);}
            }
            if(obstacle!=null&&defs.Contains(obstacle.def.defName)){
                if(pawn.CurJob!=null)pawn.jobs.StopAll();pawn.Rotation=Rot4.FromAngleFlat((obstacle.Position-pawn.Position).AngleFlat);
                if(!state.TryGetValue("obstacle",out var id)||id!=obstacle.ThingID){state["obstacle"]=obstacle.ThingID;state["elapsed"]="0";state["lastTick"]=context.Tick.ToString();return false;}
                int elapsed=Read(state,"elapsed")+Math.Min(6,Math.Max(0,context.Tick-Read(state,"lastTick",context.Tick)));
                state["elapsed"]=elapsed.ToString();state["lastTick"]=context.Tick.ToString();
                int duration=action.Parameters.TryGetValue("breachTicks",out var ticks)&&int.TryParse(ticks,out var value)?Math.Max(1,value):90;
                if(elapsed%30==0)FleckMaker.ThrowDustPuff(obstacle.DrawPos,map,.7f);
                if(elapsed<duration)return false;
                var at=obstacle.DrawPos;obstacle.Destroy(DestroyMode.KillFinalize);FleckMaker.ThrowDustPuff(at,map,1.8f);
                state.Remove("obstacle");state.Remove("elapsed");return true;
            }
            state.Remove("obstacle");state.Remove("elapsed");
            if(!next.Walkable(map)){
                // Only unconfigured obstacles may cause a local sidestep.
                var options=GenAdj.CardinalDirections.Select(d=>pawn.Position+d).Where(p=>p.InBounds(map)&&p.Walkable(map)).OrderBy(p=>p.DistanceToSquared(target.Position)).ToList();
                if(options.Count==0)return false;next=options[0];
            }
            if(pawn.CurJob?.def==JobDefOf.Goto&&pawn.CurJob.targetA.Cell==next)return false;
            var job=JobMaker.MakeJob(JobDefOf.Goto,next);job.locomotionUrgency=LocomotionUrgency.Jog;
            pawn.jobs.StartJob(job,JobCondition.InterruptForced);return true;
        }
        static bool TooManyObstacles(List<IntVec3> line,int start,Map map,string[] defs,int limit){
            var obstacles=new HashSet<Thing>();
            bool Add(IntVec3 cell){
                var thing=cell.GetEdifice(map);
                if(thing!=null&&defs.Contains(thing.def.defName))obstacles.Add(thing);
                return obstacles.Count>limit;
            }
            for(int i=start+1;i<line.Count;i++){
                var previous=line[i-1];var next=line[i];
                if(Add(next))return true;
                // Both orthogonal corner cells must be clear for native diagonal movement.
                if(previous.x!=next.x&&previous.z!=next.z){
                    if(Add(new IntVec3(next.x,0,previous.z))||Add(new IntVec3(previous.x,0,next.z)))return true;
                }
            }
            return false;
        }
        static List<IntVec3> Line(IntVec3 start,IntVec3 end){
            var result=new List<IntVec3>();int x=start.x,z=start.z,dx=Math.Abs(end.x-x),dz=Math.Abs(end.z-z),sx=Math.Sign(end.x-x),sz=Math.Sign(end.z-z),error=dx-dz;
            while(true){result.Add(new IntVec3(x,0,z));if(x==end.x&&z==end.z)break;int e=2*error;if(e>-dz){error-=dz;x+=sx;}if(e<dx){error+=dx;z+=sz;}}
            return result;
        }
    }
}
