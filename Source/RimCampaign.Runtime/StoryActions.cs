using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimCampaign.Runtime
{
    public class StoryRecord { public string Id {get;set;} public string Title {get;set;} public string Text {get;set;} }
    public partial class CampaignController
    {
        public List<StoryRecord> Records=new List<StoryRecord>();
        public string EndingText="";
        void ExposeStory(){string data=Json.Write(Records);ScribeJson.Look(ref data,"rcStoryRecords");Scribe_Values.Look(ref EndingText,"rcEndingText","");if(Scribe.mode==LoadSaveMode.LoadingVars)Records=data==null?new List<StoryRecord>():Json.Read<List<StoryRecord>>(data);}
        public void QueueStory(IEnumerable<Act> actions)=>engine.Enqueue(actions.ToList());
        public void QueueStory(IEnumerable<Act> actions,string source){if(Active&&!Transitioning&&!EntryPending)engine?.Enqueue(actions.ToList(),source:source);}
        public string MissingExtractionItems {
            get {
                return string.Join("、",MapSpec.Rewards.Where(d=>d.KeepForExtraction&&RewardStocks.Any(s=>s.Id==d.Id&&s.Collected)&&
                    d.Contents.Any(row=>Actors.Values.Where(p=>!p.Dead&&p.inventory!=null).SelectMany(p=>p.inventory.innerContainer)
                        .Where(t=>CampaignItems.Matches(t,row)).Sum(t=>t.stackCount)<row.Count)).Select(d=>RcText.T(d.Name)));
            }
        }
        public bool CanFinishStory=>State.Phase=="Running"&&!State.WantFailure&&!Actors.Values.Any(p=>p.Dead)&&StealthMapComponent.For(Map)?.State.Leaked!=true&&MissingExtractionItems.Length==0;
        public bool FinishStory(string marker,string ending=""){
            if(!CanFinishStory)return false;
            State.Fibers.Clear();State.Signals.Clear();Communications.Messages.Clear();State.WantSuccess=true;State.Phase="Succeeded";EndingText=ending;
            // Explicit story completion may settle mandatory goals; optional goals
            // must retain their actual progress in the result screen.
            foreach(var objective in MapSpec.Objectives.Where(o=>o.Required))State.Objectives[objective.Id]="Complete";
            foreach(var p in Map.mapPawns.AllPawnsSpawned)p.jobs?.StopAll();
            var destination=MapSpec.Objects.FirstOrDefault(x=>x.Id==marker);
            if(destination!=null)foreach(var p in Actors.Values.Where(p=>p.Spawned)){p.Position=new IntVec3(destination.Position.X,0,destination.Position.Z);p.Notify_Teleported();}
            StealthMapComponent.For(Map)?.RefreshVision();
            RefillCompletedMissionNeeds();
            CaptureResult();
            return true;
        }
        public void ChooseStory(Act action,int index){
            if(State.Phase!="Running"||State.Variables.ContainsKey("choice:"+action.Id)||index<0||index>=action.Steps.Count)return;
            var steps=action.Steps[index].Steps;
            if(steps.Any(a=>a.Kind=="rc.finish")&&!CanFinishStory)return;
            State.Variables["choice:"+action.Id]=index+1;
            foreach(var a in steps){if(a.Kind=="rc.finish")FinishStory(a.Target,a.Text);else QueueStory(new[]{a});}
        }
        public void ReadRecord(Act action){
            var record=Records.FirstOrDefault(r=>r.Id==action.Target);
            if(record==null){record=new StoryRecord{Id=action.Target,Title=action.Name,Text=action.Text};Records.Add(record);}
            Find.WindowStack.Add(new Dialog_MessageBox(RcText.T(record.Text),title:RcText.T(record.Title)));
        }
    }
    public sealed class ChoiceLetter_StoryChoice : ChoiceLetter
    {
        public string ActionJson,NodeId;public Map OwnerMap;
        public override void ExposeData(){base.ExposeData();Scribe_References.Look(ref OwnerMap,"rcChoiceMap");ScribeJson.Look(ref ActionJson,"rcChoiceAction");Scribe_Values.Look(ref NodeId,"rcChoiceNode");}
        public override IEnumerable<DiaOption> Choices {
            get {
                var c=CampaignController.ForMap(OwnerMap);var a=Json.Read<Act>(ActionJson);
                if(c?.Active==true&&c.NodeId==NodeId&&c.State.Phase=="Running"&&!c.State.Variables.ContainsKey("choice:"+a.Id)){
                    for(int i=0;i<a.Steps.Count;i++){
                        int index=i;
                        var option=new DiaOption(RcText.T(a.Steps[i].Text)){action=()=>{c.ChooseStory(a,index);if(c.State.Variables.ContainsKey("choice:"+a.Id))Find.LetterStack.RemoveLetter(this);},resolveTree=true};
                        if(a.Steps[i].Steps.Any(step=>step.Kind=="rc.finish")&&!c.CanFinishStory)
                            option.Disable(c.MissingExtractionItems.Length>0?RimCampaign.Runtime.RcText.T("请带上已取得的任务物品：")+c.MissingExtractionItems:RimCampaign.Runtime.RcText.T("当前无法收队。"));
                        yield return option;
                    }
                }
                yield return new DiaOption(RimCampaign.Runtime.RcText.T("关闭 / Close")){resolveTree=true};
            }
        }
    }
    [CampaignModule("rc.record","Action","保存并阅读档案：Target 编号，Name 标题，Text 正文")]
    public sealed class ReadStoryRecord : IActionModule {public bool Execute(Act a,IHost host,string source,double delta){((CampaignController)host).ReadRecord(a);return true;}}
    [CampaignModule("rc.random","Action","随机整数：Target 变量，Value 上限（含）")]
    public sealed class StoryRandom : IActionModule {public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;if(!c.State.Variables.ContainsKey(a.Target))c.State.Variables[a.Target]=Rand.RangeInclusive(1,Math.Max(1,(int)a.Value));return true;}}
    [CampaignModule("rc.choice","Action","蓝色选择信：Name 标题，Text 正文，Steps 为选项及其动作")]
    public sealed class StoryChoice : IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;
            if(c.State.Variables.ContainsKey("choice:"+a.Id))return true;
            if(!Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_StoryChoice>().Any(l=>l.ActionJson==Json.Write(a)&&l.NodeId==c.NodeId)){
                var letter=(ChoiceLetter_StoryChoice)LetterMaker.MakeLetter(RcText.T(a.Name),RcText.T(a.Text),DefDatabase<LetterDef>.GetNamed("RC_StoryChoice"));
                letter.ActionJson=Json.Write(a);letter.NodeId=c.NodeId;letter.OwnerMap=c.Map;Find.LetterStack.ReceiveLetter(letter);
            }return true;
        }
    }
    [CampaignModule("rc.finish","Action","立即完成关卡，Target 可选撤离标记（省略回程）")]
    public sealed class StoryFinish : IActionModule {public bool Execute(Act a,IHost host,string source,double delta){((CampaignController)host).FinishStory(a.Target,a.Text);return true;}}
    [CampaignModule("rc.device","Action","设备控制：Target 设备ID，Text 为 capture/disable/enable")]
    public sealed class StoryDevice : IActionModule {public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;var s=StealthMapComponent.For(c.Map);if(!s.State.Devices.TryGetValue(a.Target,out var d))throw new InvalidOperationException("Missing device "+a.Target);if(a.Text=="capture"){d.Captured=true;d.ReportProgress=0;}else d.Disabled=a.Text!="enable";s.RefreshVision();return true;}}
    [CampaignModule("rc.power","Action","设定建筑电量与开关：Target 建筑，Value 为0关闭或1开启")]
    public sealed class StoryPower : IActionModule {public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;var thing=c.Objects[a.Target];var battery=thing.TryGetComp<CompPowerBattery>();if(battery!=null)battery.AddEnergy(battery.Props.storedEnergyMax);var power=thing.TryGetComp<CompPowerTrader>();if(power!=null)power.PowerOn=a.Value>0;var flick=thing.TryGetComp<CompFlickable>();if(flick!=null)flick.SwitchIsOn=a.Value>0;return true;}}
    [CampaignModule("rc.unlock","Action","解锁门：Target 门；开启并保持通行")]
    public sealed class StoryUnlock : IActionModule {public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;var door=c.Objects[a.Target] as Building_Door;if(door==null)throw new InvalidOperationException("Target is not a door");door.SetFaction(Faction.OfPlayer);door.SetForbidden(false,false);return true;}}
    [HarmonyLib.HarmonyPatch(typeof(Pawn),nameof(Pawn.GetGizmos))]
    static class StoryJournalGizmo {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values,Pawn __instance){foreach(var g in values)yield return g;var c=CampaignController.ForMap(__instance.MapHeld);if(c?.Active!=true||!c.Actors.Values.Contains(__instance)||__instance.Dead||__instance.Downed)yield break;
            yield return new Command_Action{defaultLabel=RimCampaign.Runtime.RcText.T("收取随身物资"),defaultDesc=RimCampaign.Runtime.RcText.T("选择可见的物品，前往拾取并收入背包，供后续关卡使用。"),icon=TexCommand.DesirePower,action=()=>Find.Targeter.BeginTargeting(new TargetingParameters{canTargetItems=true,canTargetBuildings=false,canTargetPawns=false,canTargetLocations=false,validator=t=>t.Thing?.def.category==ThingCategory.Item&&StealthMapComponent.For(__instance)?.Visible(t.Cell)!=false},t=>{var job=JobMaker.MakeJob(JobDefOf.TakeInventory,t.Thing);job.count=t.Thing.stackCount;job.playerForced=true;__instance.jobs.TryTakeOrderedJob(job,JobTag.Misc);})};
            if(c.Records.Count==0)yield break;
            yield return new Command_Action{defaultLabel=RimCampaign.Runtime.RcText.T("调查档案 / Records"),icon=TexCommand.DesirePower,action=()=>Find.WindowStack.Add(new FloatMenu(c.Records.Select(r=>new FloatMenuOption(RcText.T(r.Title),()=>Find.WindowStack.Add(new Dialog_MessageBox(RcText.T(r.Text),title:RcText.T(r.Title))))).ToList()))};
        }
    }
}
