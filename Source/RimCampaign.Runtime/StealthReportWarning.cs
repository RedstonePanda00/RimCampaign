using System.Linq;
using RimCampaign.Core;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime
{
    public sealed partial class StealthMapComponent
    {
        Letter reportLetter;
        bool IsReporting(GuardSpec guard)
        {
            if(State.Leaked||!State.Guards.TryGetValue(guard.Id,out var memory)||memory.Mode!="Reporting"||memory.Reported)return false;
            var pawn=Resolve(guard.Pawn) as Pawn;
            return pawn?.Spawned==true&&!pawn.Dead&&!pawn.Downed&&!Sleeping(pawn)&&
                !(State.PendingKind=="Takedown"&&State.PendingTarget==pawn.ThingID)&&
                DeviceWorking(Config.Devices.FirstOrDefault(d=>d.Id==guard.Uplink));
        }
        public Pawn WarningReporter {
            get {
                var guard=Config?.Guards.FirstOrDefault(g=>g.Id==State.ReportWarningGuard);
                return guard!=null&&IsReporting(guard)?Resolve(guard.Pawn) as Pawn:null;
            }
        }
        bool WarnFirstReport(GuardSpec guard,Pawn pawn)
        {
            if(State.FirstReportWarned||State.Leaked)return false;
            State.FirstReportWarned=true;State.ReportWarningGuard=guard.Id;
            RefreshVision();
            Current.Game.CurrentMap=map;
            Find.CameraDriver.SetRootPosAndSize(pawn.DrawPos,18);
            Find.TickManager.Pause();
            reportLetter=LetterMaker.MakeLetter(RimCampaign.Runtime.RcText.T("阻止敌人汇报"),
                RimCampaign.Runtime.RcText.T("一名守卫正赶往通讯设备，准备向外汇报入侵。\n\n镜头已定位到该守卫，并临时提供其周围的局部视野。请在汇报完成前制伏或击倒他，或切断对应的通讯设备。重新接战也会打断汇报。\n\n游戏已暂停，请部署拦截行动后继续。此次提示只出现一次。"),
                LetterDefOf.ThreatBig,pawn);
            Find.LetterStack.ReceiveLetter(reportLetter);
            Emit("ReportWarning",guard.Id);
            return true;
        }
    }
}
