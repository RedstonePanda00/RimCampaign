using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimCampaign.Runtime
{
    public sealed partial class StealthMapComponent
    {
        public void KnockOut(Pawn pawn)
        {
            if(pawn==null||pawn.Dead||Sleeping(pawn))return;
            State.Unconscious.Add(pawn.ThingID);pawn.jobs.StopAll();pawn.mindState.enemyTarget=null;
            pawn.health.AddHediff(DefDatabase<HediffDef>.GetNamed("RC_Unconscious"));
            var guard=Guard(pawn);if(guard!=null){State.Guards[guard.Id].ReportProgress=0;State.Guards[guard.Id].RadioAt=-1;}
            if(guard!=null)CampaignController.ForMap(pawn.Map)?.RecordPerformance("subdue",pawn.ThingID);
            Emit("Subdued",State.Bindings.FirstOrDefault(b=>b.Value==pawn.ThingID).Key??pawn.ThingID);
        }
        bool IsBehind(Pawn target)=>Actor!=null&&!StealthMath.InCone(Cell(target.Position),Cell(Actor.Position),target.Rotation.AsInt,3,200);
        public bool CanSubdue(Pawn target)=>Config.AllowTakedown&&target?.Spawned==true&&!target.Dead&&!target.Downed&&!Sleeping(target)&&
            Actor?.Spawned==true&&!Actor.Downed&&target!=Actor&&StealthMath.InTakedownReach(Cell(Actor.Position),Cell(target.Position))&&IsBehind(target)&&
            (Guard(target)==null||State.Guards[Guard(target).Id].Mode!="Combat")&&GenSight.LineOfSight(Actor.Position,target.Position,map);
        void Reject(string message)=>Messages.Message(message,MessageTypeDefOf.RejectInput,false);
        public void BeginTakedown(Pawn target)
        {
            if(!CanSubdue(target)){Reject(RimCampaign.Runtime.RcText.T("需要位于未警觉目标背后，目标须在周围相邻八格或四向直线第二格内，且不能隔墙。"));return;}
            CancelInteraction();State.PendingKind="Takedown";State.PendingTarget=target.ThingID;resolved[target.ThingID]=target;
            Actor.jobs.StopAll();target.jobs.StopAll();Wait(Actor);Wait(target);
        }
        public void CancelInteraction(){State.PendingKind="";State.PendingTarget="";State.PendingProgress=0;if(Actor?.CurJob?.def.defName=="RC_UseCampaignDevice")Actor.jobs.EndCurrentJob(JobCondition.InterruptForced);}
        public bool CanOperate(Pawn pawn)=>pawn?.Spawned==true&&pawn.Map==map&&pawn.Faction==Faction.OfPlayer&&!pawn.Dead&&!pawn.Downed&&!pawn.InMentalState&&(pawn.RaceProps.Humanlike||pawn.RaceProps.IsMechanoid)&&pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        public bool DeviceOpened(DeviceSpec d)=>Resolve(d.Host) is Building_Crate crate?!crate.HasAnyContents:State.Devices[d.Id].Opened;
        public void SelectDeviceOperator(DeviceSpec device)=>Find.Targeter.BeginTargeting(new TargetingParameters{canTargetPawns=true,canTargetLocations=false,canTargetBuildings=false,canTargetItems=false,validator=t=>t.Thing is Pawn p&&CanOperate(p)},t=>BeginDevice(device,t.Pawn));
        public bool BeginDevice(DeviceSpec device,Pawn executor=null)
        {
            executor=executor??Actor;
            if(device==null||!Active||DeviceUnavailableReason(device)!=null||!CanOperate(executor)||!Visible(DevicePosition(device)))return false;
            LocalTargetInfo target=Resolve(device.Host) is Thing host?new LocalTargetInfo(host):new LocalTargetInfo(DevicePosition(device));
            if(!executor.CanReach(target,PathEndMode.Touch,Danger.Deadly)||!executor.CanReserve(target)){Reject(RimCampaign.Runtime.RcText.T("所选角色无法抵达或预约该目标。"));return false;}
            if(executor==Actor&&State.PendingKind=="Takedown")CancelInteraction();
            var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RC_UseCampaignDevice"),target);job.playerForced=true;job.count=Config.Devices.IndexOf(device);
            bool accepted=executor.jobs.TryTakeOrderedJob(job,JobTag.Misc);if(accepted)ownedJobs[executor.ThingID]=job.loadID;return accepted;
        }
        void TickInteraction()
        {
            var actor=Actor;if(actor?.Spawned!=true||actor.Dead||actor.Downed){CancelInteraction();return;}
            if(State.PendingKind=="Takedown"){
                var target=Resolve(State.PendingTarget) as Pawn;
                if(!CanSubdue(target)||actor.CurJob?.def!=JobDefOf.Wait){CancelInteraction();return;}
                // Generic paired-action hook: content mods may replace the visual without changing its outcome.
                if(++State.PendingProgress>=Config.TakedownTicks){KnockOut(target);Emit("InteractionCompleted","takedown",target.ThingID);CancelInteraction();}
                else if(target.CurJob?.def!=JobDefOf.Wait)Wait(target);
            }
        }
        public void CompleteDevice(DeviceSpec d,Pawn executor=null)
        {
            if(!DeviceWorking(d)||!DeviceRequirementMet(d)||(d.OneShot&&State.Devices[d.Id].Opened))return;
            executor=executor??Actor;var state=State.Devices[d.Id];
            switch(d.Kind){
                case "SupplyCrate":if(Resolve(d.Host) is Building_Crate crate){if(crate.CanOpen)crate.Open();return;}if(state.Opened)return;state.Opened=true;break;
                case "Camera":state.Captured=true;state.ReportProgress=0;Emit("CameraCaptured",d.Id);RefreshVision();break;
                case "Uplink":state.Disabled=true;state.ReportProgress=0;Emit("UplinkDisabled",d.Id);break;
                case "Broadcast":state.Captured=true;EmitNoise(DevicePosition(d),d.Radius);break;
                case "Password":OpenPassword(d,executor);return;
            }
            if(d.OneShot)state.Opened=true;
            EmitDeviceCompleted(d,executor);
        }
        public bool DeviceRequirementMet(DeviceSpec d){
            if(string.IsNullOrEmpty(d.RequiredVariable))return true;
            var c=CampaignController.ForMap(map);if(c==null)return false;
            return (c.State.Variables.TryGetValue(d.RequiredVariable,out var value)?value:c.Read(d.RequiredVariable))>0;
        }
        public void EmitDeviceCompleted(DeviceSpec d,Pawn executor){string source=executor?.ThingID??"";Emit("InteractionCompleted",d.Id,source);if(d.Signal!="")Emit("Signal",d.Signal,source);}
        void OpenPassword(DeviceSpec d,Pawn executor)
        {
            var controller=CampaignController.ForMap(map);
            Find.WindowStack.Add(new PasswordWindow(d.Name,input=>{
                if(controller?.Active!=true||controller.Map!=map)return PasswordSubmission.NotRunning;
                if(!CanOperate(executor))return PasswordSubmission.OperatorUnavailable;
                if(!executor.CanReachImmediate((Resolve(d.Host) is Thing host?new LocalTargetInfo(host):new LocalTargetInfo(DevicePosition(d))),PathEndMode.Touch))return PasswordSubmission.OutOfReach;
                if(!DeviceWorking(d))return PasswordSubmission.DeviceUnavailable;
                var result=controller.SubmitPasswordDetailed(d.PasswordRule,input,executor.ThingID);
                if(result==PasswordSubmission.Accepted)EmitDeviceCompleted(d,executor);return result;
            }));
        }
        public IEnumerable<Gizmo> Commands(Pawn pawn)
        {
            if(!Active||pawn.Dead||pawn.Downed)yield break;
            if(pawn.jobs?.curDriver is JobDriver_UseCampaignDevice)yield return new Command_Action{defaultLabel=RimCampaign.Runtime.RcText.T("取消建筑操作"),icon=TexCommand.ClearPrioritizedWork,action=()=>pawn.jobs.EndCurrentJob(JobCondition.InterruptForced)};
            if(pawn!=Actor)yield break;
            Command_Action Command(string title,string desc,Action action)=>new Command_Action{defaultLabel=title,defaultDesc=desc,
                icon=title.StartsWith(RimCampaign.Runtime.RcText.T("背后"))?TexCommand.AttackMelee:TexCommand.ClearPrioritizedWork,action=action};
            void Target(Action<LocalTargetInfo> action,bool pawns=false)=>Find.Targeter.BeginTargeting(new TargetingParameters{canTargetLocations=!pawns,canTargetPawns=pawns,canTargetBuildings=!pawns,canTargetItems=!pawns},action);
            yield return new Command_Toggle{defaultLabel=RimCampaign.Runtime.RcText.T("允许自动近战"),defaultDesc=RimCampaign.Runtime.RcText.T("关闭时不会主动殴打身边的敌人。手动近战命令与背后制伏仍然可用。"),icon=TexCommand.AttackMelee,isActive=()=>State.AllowAutomaticMelee,toggleAction=()=>State.AllowAutomaticMelee=!State.AllowAutomaticMelee};
            if(Config.AllowTakedown)yield return Command(RimCampaign.Runtime.RcText.T("背后制伏"),RimCampaign.Runtime.RcText.T("位于未警觉守卫背后，周围相邻八格及四向直线第二格均可发动，不受自身朝向限制；不能隔墙。"),()=>Target(t=>BeginTakedown(t.Pawn),true));
            if(State.PendingKind!="")yield return Command(RimCampaign.Runtime.RcText.T("取消交互"),RimCampaign.Runtime.RcText.T("中断当前设备操作或制伏。"),CancelInteraction);
        }
        public override void MapComponentOnGUI()
        {
            if(!Active||CinematicReveal||Find.CurrentMap!=map)return;
            DrawConversations();
            foreach(var g in Config.Guards){var p=Resolve(g.Pawn) as Pawn;if(p?.Spawned!=true||!PlayerCanTarget(p)||p.Dead)continue;
                var s=State.Guards[g.Id];var pos=GenMapUI.LabelDrawPosFor(p,-.6f);
                if(Sleeping(p))Widgets.Label(new Rect(pos.x-25,pos.y-26,80,28),RimCampaign.Runtime.RcText.T("昏迷"));
                else if(!p.Downed)DrawDetectionProgress(pos,(float)s.Suspicion);
            }
            foreach(var d in Config.Devices.Where(d=>d.Kind=="Camera"&&DeviceWorking(d)&&!State.Devices[d.Id].Captured&&Visible(DevicePosition(d)))){
                var host=Resolve(d.Host);
                if(host!=null)DrawDetectionProgress(GenMapUI.LabelDrawPosFor(host,-.6f),(float)State.Devices[d.Id].DetectionProgress/Math.Max(1,d.DetectionTicks));
            }
        }
        static void DrawDetectionProgress(Vector2 position,float progress)
        {
            var color=progress>=1?Color.red:progress>=.5f?Color.yellow:Color.white;
            var center=new Vector2(position.x,position.y-28);
            for(int i=0;i<48;i++){
                float a=i*Mathf.PI/24-Mathf.PI/2,b=(i+1)*Mathf.PI/24-Mathf.PI/2;
                var from=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*11;
                var to=center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*11;
                Widgets.DrawLine(from,to,new Color(0,0,0,.8f),5);
                Widgets.DrawLine(from,to,i/48f<progress?color:new Color(1,1,1,.3f),i/48f<progress?3:1);
            }
        }
        public override void MapComponentDraw()
        {
            if(!Active||CinematicReveal||Find.CurrentMap!=map)return;
            DrawRememberedFog();
            if(!State.Leaked){
                foreach(var g in Config.Guards){var guard=Resolve(g.Pawn) as Pawn;
                    if(guard?.Spawned==true&&!guard.Dead&&!guard.Downed&&!Sleeping(guard)&&PlayerCanTarget(guard))
                        GenDraw.DrawFieldEdges(GenRadial.RadialCellsAround(guard.Position,g.SightRange,true).Where(cell=>GuardCanSee(g,cell)).ToList(),Color.yellow,renderQueue:3700);
                }
                foreach(var d in Config.Devices.Where(d=>d.Kind=="Camera"&&DeviceWorking(d)&&!State.Devices[d.Id].Captured&&Visible(DevicePosition(d))))DrawVision(DevicePosition(d),d.Facing,d.SightRange,d.ViewAngle,new Color(1,.5f,0));
            }
        }
        void DrawVision(IntVec3 from,int facing,float range,float angle,Color color)=>GenDraw.DrawFieldEdges(GenRadial.RadialCellsAround(from,range,true).Where(c=>CanSee(from,c,facing,range,angle)).ToList(),color);
    }
    public sealed class PasswordWindow : Window
    {
        readonly string title;readonly Func<string,PasswordSubmission> submit;string input="",error="";
        public override Vector2 InitialSize=>new Vector2(460,230);
        public PasswordWindow(string title,Func<string,PasswordSubmission> submit){this.title=title;this.submit=submit;forcePause=true;doCloseX=true;absorbInputAroundWindow=true;closeOnAccept=false;}
        public static string RejectionText(PasswordSubmission result){
            switch(result){
                case PasswordSubmission.Mismatch:return RcText.T("密码不正确，请重新输入。");
                case PasswordSubmission.NotRunning:return RcText.T("当前任务已结束，无法提交密码。");
                case PasswordSubmission.OperatorUnavailable:return RcText.T("操作角色当前无法使用设备。");
                case PasswordSubmission.OutOfReach:return RcText.T("操作角色已离开设备，请靠近后重新操作。");
                case PasswordSubmission.DeviceUnavailable:return RcText.T("设备已损毁或停用。");
                case PasswordSubmission.AlreadySubmitted:return RcText.T("密码已提交或此门已解锁，请关闭窗口。");
                case PasswordSubmission.ConditionsNotMet:return RcText.T("尚未满足此设备的解锁条件。");
                case PasswordSubmission.Disabled:return RcText.T("密码触发器已停用。");
                default:return RcText.T("此设备缺少有效的密码触发器配置。");
            }
        }
        public override void DoWindowContents(Rect r){
            bool enter=Event.current.type==EventType.KeyDown&&(Event.current.keyCode==KeyCode.Return||Event.current.keyCode==KeyCode.KeypadEnter);
            if(enter)Event.current.Use();
            Widgets.Label(new Rect(0,0,r.width,30),title);
            input=Widgets.TextField(new Rect(0,40,r.width,32),input);
            Widgets.Label(new Rect(0,80,r.width,50),error);
            if(Widgets.ButtonText(new Rect(0,140,r.width*.65f,35),RimCampaign.Runtime.RcText.T("提交 / Submit"))||enter){
                var result=submit(input);if(result==PasswordSubmission.Accepted)Close();else error=RejectionText(result);
            }
            if(Widgets.ButtonText(new Rect(r.width*.68f,140,r.width*.32f,35),RimCampaign.Runtime.RcText.T("取消")))Close();
        }
    }
    public partial class CampaignController
    {
        public bool SubmitPassword(string rule,string input,string source="")=>engine?.SubmitPassword(rule,input,source)==true;
        public PasswordSubmission SubmitPasswordDetailed(string rule,string input,string source="")=>engine?.SubmitPasswordDetailed(rule,input,source)??PasswordSubmission.NotRunning;
    }
}
