using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimCampaign.Runtime
{
    public sealed class JobDriver_UseCampaignDevice : JobDriver
    {
        string deviceId;
        StealthMapComponent Component=>StealthMapComponent.For(pawn);
        DeviceSpec Device=>Component?.Config.Devices.FirstOrDefault(d=>d.Id==deviceId);
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref deviceId,"campaignDeviceId");}
        public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.Reserve(job.targetA,job,1,-1,null,errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            if(deviceId==null)deviceId=Component?.Config.Devices.ElementAtOrDefault(job.count)?.Id;
            this.FailOn(()=>Component==null||Device==null||!Component.DeviceWorking(Device)||pawn.Downed||pawn.Dead||!Component.CanOperate(pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
            var use=Toils_General.Wait(Device?.UseTicks??1,TargetIndex.A);
            use.WithProgressBarToilDelay(TargetIndex.A);
            
            use.FailOn(()=>!pawn.CanReachImmediate(job.targetA,PathEndMode.Touch));
            yield return use;
            var finish=ToilMaker.MakeToil("CompleteCampaignDevice");finish.defaultCompleteMode=ToilCompleteMode.Instant;
            finish.initAction=()=>{var c=Component;var d=Device;if(c!=null&&d!=null)c.CompleteDevice(d,pawn);};
            yield return finish;
        }
    }
    public sealed partial class StealthMapComponent
    {
        string DeviceCommandLabel(DeviceSpec d)=>!string.IsNullOrEmpty(d.CommandLabel)?RcText.T(d.CommandLabel):
            d.Kind=="SupplyCrate"?RimCampaign.Runtime.RcText.T("打开补给箱"):d.Kind=="Camera"?RimCampaign.Runtime.RcText.T("接管摄像头"):d.Kind=="Uplink"?RimCampaign.Runtime.RcText.T("切断对外通讯"):
            d.Kind=="Password"?RimCampaign.Runtime.RcText.T("输入密码"):d.Kind=="Broadcast"?(State.Devices[d.Id].Captured?RimCampaign.Runtime.RcText.T("播放广播"):RimCampaign.Runtime.RcText.T("控制广播")):RimCampaign.Runtime.RcText.T("操作");
        string DeviceUnavailableReason(DeviceSpec d)
        {
            var state=State.Devices[d.Id];
            if(!DeviceWorking(d))return RimCampaign.Runtime.RcText.T("设备已停用。");
            if(!DeviceRequirementMet(d))return d.RequirementText!=null?RcText.T(d.RequirementText):RimCampaign.Runtime.RcText.T("尚未满足操作条件。");
            if(d.OneShot&&state.Opened)return RimCampaign.Runtime.RcText.T("本次操作已完成。");
            if(d.Kind=="SupplyCrate"&&DeviceOpened(d))return RimCampaign.Runtime.RcText.T("补给已领取。");
            if(d.Kind=="Camera"&&state.Captured)return RimCampaign.Runtime.RcText.T("摄像头已接管。");
            if(d.Kind=="Password"&&string.IsNullOrEmpty(d.PasswordRule))return RimCampaign.Runtime.RcText.T("尚未配置密码触发器。");
            return null;
        }
        public IEnumerable<FloatMenuOption> DeviceOptions(Building building,Pawn executor)
        {
            if(!Active||!Visible(building.Position)||!CanOperate(executor))yield break;
            foreach(var d in Config.Devices.Where(d=>Resolve(d.Host)==building)){
                var reason=DeviceUnavailableReason(d);
                if(reason==null&&!executor.CanReach(building,PathEndMode.Touch,Danger.Deadly))reason=RimCampaign.Runtime.RcText.T("无法抵达。");
                if(reason==null&&!executor.CanReserve(building))reason=RimCampaign.Runtime.RcText.T("无法预约。");
                var label=DeviceCommandLabel(d);
                yield return reason!=null?new FloatMenuOption(label+"："+reason,null):
                    new FloatMenuOption(label,()=>BeginDevice(d,executor));
            }
        }
        public IEnumerable<Gizmo> DeviceCommands(Building building)
        {
            if(!Active||!Visible(building.Position))yield break;
            foreach(var d in Config.Devices.Where(d=>Resolve(d.Host)==building)){
                var command=new Command_Action{defaultLabel=DeviceCommandLabel(d),
                    defaultDesc=d.Name+RimCampaign.Runtime.RcText.T("\n点击后选择执行角色；前往目标操作，耗时 ")+(d.UseTicks/60f).ToString("0.0")+RimCampaign.Runtime.RcText.T(" 秒。"),icon=building.def.uiIcon,
                    action=()=>SelectDeviceOperator(d)};
                var reason=DeviceUnavailableReason(d);
                if(reason!=null)command.Disable(reason);
                else if(!map.mapPawns.AllPawnsSpawned.Any(CanOperate))command.Disable(RimCampaign.Runtime.RcText.T("没有可执行操作的己方角色。"));
                yield return command;
            }
        }
        public string DeviceDescription(Building building)
        {
            var lines=new List<string>();
            foreach(var d in Config.Devices.Where(d=>Resolve(d.Host)==building)){
                var s=State.Devices[d.Id];
                lines.Add(d.Name+"："+(s.Disabled?RimCampaign.Runtime.RcText.T("已停用"):d.Kind=="SupplyCrate"&&DeviceOpened(d)?RimCampaign.Runtime.RcText.T("已领取"):s.Captured?RimCampaign.Runtime.RcText.T("已控制"):RimCampaign.Runtime.RcText.T("可操作")));
                if(d.Kind=="Camera")lines.Add(d.AutoReport?RimCampaign.Runtime.RcText.T("自动对外传输：开启（需要通讯设备）"):RimCampaign.Runtime.RcText.T("自动对外传输：关闭；由清醒守卫报告"));
                if(s.ReportProgress>0)lines.Add(RimCampaign.Runtime.RcText.T("外传剩余：")+Math.Max(0,(d.ReportTicks-s.ReportProgress)/60f).ToString("0.0")+RimCampaign.Runtime.RcText.T(" 秒"));
            }
            return string.Join("\n",lines);
        }
    }
    public sealed class FloatMenuOptionProvider_CampaignDevice : FloatMenuOptionProvider
    {
        protected override bool Drafted=>true;
        protected override bool Undrafted=>true;
        protected override bool Multiselect=>false;
        protected override bool RequiresManipulation=>true;
        protected override bool MechanoidCanDo=>true;
        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing,FloatMenuContext context)
        {
            if(!(clickedThing is Building building))yield break;
            var component=StealthMapComponent.For(building.Map);
            if(component!=null)foreach(var option in component.DeviceOptions(building,context.FirstSelectedPawn))yield return option;
        }
    }
    [HarmonyPatch(typeof(Building),nameof(Building.GetGizmos))]
    static class StealthBuildingGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values,Building __instance){
            foreach(var g in values)yield return g;
            var c=StealthMapComponent.For(__instance.Map);if(c!=null)foreach(var g in c.DeviceCommands(__instance))yield return g;
        }
    }
    [HarmonyPatch(typeof(ThingWithComps),nameof(ThingWithComps.GetInspectString))]
    static class StealthBuildingInspection
    {
        static void Postfix(ThingWithComps __instance,ref string __result){
            if(!(__instance is Building building))return;
            var text=StealthMapComponent.For(building.Map)?.DeviceDescription(building);
            if(!string.IsNullOrEmpty(text))__result=(__result+"\n"+text).Trim();
        }
    }
    // Scope the switch to vanilla opportunistic melee; explicit melee jobs and abilities are unaffected.
    [HarmonyPatch(typeof(JobDriver_Wait),"CheckForAutoAttack")]
    static class StealthAutomaticAttackScope
    {
        [ThreadStatic] internal static Pawn Attacker;
        static void Prefix(JobDriver_Wait __instance,out Pawn __state){__state=Attacker;Attacker=__instance.pawn;}
        static Exception Finalizer(Exception __exception,Pawn __state){Attacker=__state;return __exception;}
    }
    [HarmonyPatch(typeof(Pawn_MeleeVerbs),nameof(Pawn_MeleeVerbs.TryMeleeAttack))]
    static class StealthAutomaticMelee
    {
        static bool Prefix(Pawn ___pawn,ref bool __result){
            var c=StealthMapComponent.For(___pawn);
            var guard=c?.Guard(___pawn);
            if(guard!=null&&!c.CanAttackActor(guard)){__result=false;return false;}
            if(c?.Actor==___pawn&&!c.State.AllowAutomaticMelee&&StealthAutomaticAttackScope.Attacker==___pawn){__result=false;return false;}
            return true;
        }
    }
}
