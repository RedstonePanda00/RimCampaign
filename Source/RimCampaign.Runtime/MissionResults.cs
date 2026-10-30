using System;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime
{
    public partial class CampaignController
    {
        public MissionResult Result;
        public void RefillCompletedMissionNeeds()
        {
            if(State.Phase!="Succeeded"||!Node.RefillNeedsOnSuccess)return;
            foreach(var pawn in Actors.Values.Where(p=>!p.Dead)){
                if(pawn.needs?.food!=null)pawn.needs.food.CurLevelPercentage=1;
                if(pawn.needs?.rest!=null)pawn.needs.rest.CurLevelPercentage=1;
                if(pawn.needs?.joy!=null)pawn.needs.joy.CurLevelPercentage=1;
            }
        }
        public void RecordPerformance(string kind,string id)
        {
            if(Active&&Node.Results!=null&&State.Phase=="Running")State.Performance.Record(kind,id);
        }
        void ExposeResult()
        {
            string data=Result==null?null:Json.Write(Result);ScribeJson.Look(ref data,"rcMissionResult");
            if(Scribe.mode==LoadSaveMode.LoadingVars)Result=data==null?null:Json.Read<MissionResult>(data);
        }
        public void CaptureResult()
        {
            if(Node.Results==null||Result?.Node==NodeId)return;
            Result=new MissionResult{Node=NodeId,Title=string.IsNullOrEmpty(Node.Results.Title)?Node.Name:Node.Results.Title,Summary=EndingText,Seconds=State.Elapsed,Performance=Json.Copy(State.Performance)};
            foreach(var objective in MapSpec.Objectives)
                Result.Objectives.Add(new ResultRow{Label=objective.Name,Value=State.Objectives.TryGetValue(objective.Id,out var state)&&state=="Complete"?RimCampaign.Runtime.RcText.T("已完成"):RimCampaign.Runtime.RcText.T("未完成")});
            foreach(var device in MapSpec.Rewards.Where(d=>d.KeepForExtraction))
                Result.Objectives.Add(new ResultRow{Label=device.Name,Value=RewardStocks.Any(s=>s.Id==device.Id&&s.Collected)?RimCampaign.Runtime.RcText.T("已取得"):RimCampaign.Runtime.RcText.T("未取得")});
            var cargo=Actors.Values.Where(p=>!p.Dead).SelectMany(p=>p.inventory==null?Enumerable.Empty<Thing>():p.inventory.innerContainer.AsEnumerable());
            foreach(var group in cargo.GroupBy(t=>t.LabelNoCount).OrderBy(g=>g.Key))Result.Cargo.Add(new ResultRow{Label=group.Key,Value="× "+group.Sum(t=>t.stackCount)});
        }
        public void ShowResult()
        {
            CaptureResult();Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
            if(!Find.WindowStack.Windows.OfType<MissionResultWindow>().Any()){
                var type=string.IsNullOrEmpty(Node.Results.PresentationType)?typeof(MissionResultWindow):AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(Node.Results.PresentationType,false)).FirstOrDefault(t=>t!=null);
                if(type==null||!typeof(MissionResultWindow).IsAssignableFrom(type))throw new InvalidOperationException("Invalid campaign result presentation: "+Node.Results.PresentationType);
                Find.WindowStack.Add((MissionResultWindow)Activator.CreateInstance(type,this));
            }
        }
        public bool HasNextMission=>engine.Next(Campaign,NodeId,"success")!=null;
        public void ContinueResult()
        {
            if(State.Phase!="Succeeded")return;
            var edge=engine.Next(Campaign,NodeId,"success");if(edge!=null)Transition(edge.To);
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.Kill))]
    static class MissionResultDeath
    {
        static void Prefix(Pawn __instance,out CampaignController __state)
        {
            var c=CampaignController.ForMap(__instance.MapHeld);
            __state=c?.Active==true&&c.Map==__instance.MapHeld&&StealthMapComponent.For(c.Map)?.Guard(__instance)!=null&&!__instance.Dead?c:null;
        }
        static void Postfix(Pawn __instance,CampaignController __state)
        {
            if(__instance.Dead)__state?.RecordPerformance("kill",__instance.ThingID);
        }
    }
    [HarmonyPatch(typeof(LearningReadout),nameof(LearningReadout.LearningReadoutOnGUI))]
    static class MissionResultLearningOverlay
    {
        static bool Prefix()=>Find.WindowStack==null||!Find.WindowStack.Windows.OfType<MissionResultWindow>().Any();
    }
}
