using System;
using RimCampaign.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Runtime
{
    // Neutral native presentation. Campaign mods subclass this window to supply
    // their own layout, typography, textures and rendering effects.
    public class MissionResultWindow : Window
    {
        protected readonly CampaignController controller;
        protected readonly MissionResult result;
        protected readonly ResultSpec spec;
        Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(820,640);
        public virtual bool NativeShaderReady=>false;
        public virtual void Reveal(){ }
        public MissionResultWindow(CampaignController controller)
        {
            this.controller=controller;result=controller.Result;spec=controller.Node.Results;
            forcePause=true;absorbInputAroundWindow=true;closeOnCancel=false;closeOnAccept=false;preventCameraMotion=true;
        }
        public override void DoWindowContents(Rect r)
        {
            var font=Text.Font;Text.Font=GameFont.Medium;Widgets.Label(new Rect(0,0,r.width,40),RcText.T(spec.Heading)+" — "+RcText.T(result.Title));Text.Font=font;
            float scoreHeight=spec.StealthRating?60:0;
            if(spec.StealthRating)Widgets.Label(new Rect(0,45,r.width,60),RimCampaign.Runtime.RcText.T("潜行表现：")+result.Performance.Rank(spec)+"  "+result.Performance.Score(spec)+RimCampaign.Runtime.RcText.T(" / 100\n识破来源：")+result.Performance.DetectedBy.Count+RimCampaign.Runtime.RcText.T("   汇报者：")+result.Performance.Reporters.Count+RimCampaign.Runtime.RcText.T("   死亡：")+result.Performance.Killed.Count+RimCampaign.Runtime.RcText.T("   非致命制伏：")+result.Performance.SurvivingSubdued);
            float top=50+scoreHeight;var area=new Rect(0,top,r.width,r.height-top-65);
            string content=RcText.T(result.Summary)+RimCampaign.Runtime.RcText.T("\n\n行动用时：")+TimeSpan.FromSeconds(result.Seconds).ToString(@"hh\:mm\:ss")+"\n";
            foreach(var row in result.Objectives)content+="\n"+RcText.T(row.Label)+": "+RcText.T(row.Value);
            content+=RimCampaign.Runtime.RcText.T("\n\n随身物资\n");foreach(var row in result.Cargo)content+="\n"+row.Label+" "+row.Value;
            var view=new Rect(0,0,area.width-20,Mathf.Max(area.height,Text.CalcHeight(content,area.width-20)));
            Widgets.BeginScrollView(area,ref scroll,view);Widgets.Label(view,content);Widgets.EndScrollView();
            float w=(r.width-24)/4,y=r.height-45;
            if(Widgets.ButtonText(new Rect(0,y,w,38),RimCampaign.Runtime.RcText.T("保存进度")))Find.WindowStack.Add(new Dialog_SaveFileList_Save());
            if(Widgets.ButtonText(new Rect(w+8,y,w,38),RimCampaign.Runtime.RcText.T("重试本关"))){Close();controller.Retry();}
            if(Widgets.ButtonText(new Rect((w+8)*2,y,w,38),RimCampaign.Runtime.RcText.T("返回主菜单")))Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(RimCampaign.Runtime.RcText.T("返回主菜单？请先保存需要保留的通关进度。"),()=>GenScene.GoToMainMenu()));
            if(controller.HasNextMission&&Widgets.ButtonText(new Rect((w+8)*3,y,w,38),RimCampaign.Runtime.RcText.T("继续战役"))){Close();controller.ContinueResult();}
        }
    }
}
