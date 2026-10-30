using System;
using System.Collections.Generic;
using RimCampaign.SDK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimCampaign.Runtime {
    public partial class CampaignController {
        string openingTitle="";
        List<string> openingLines=new List<string>();
        float openingElapsed=-1,openingLastFrame=-1;
        GUIStyle openingHeadingStyle,openingLineStyle;
        Color openingHeadingColor,openingLineColor;
        GUIContent[] openingContents;
        Rect[] openingRects;
        int openingWidth=-1,openingHeight=-1;
        bool openingCinematic;
        public float OpeningTitleElapsed=>openingElapsed;
        public string OpeningTitleText=>openingTitle;
        public IReadOnlyList<string> OpeningTitleLines=>openingLines;
        public Rect? OpeningTitleBounds=>OpeningTitleAlpha>0&&openingRects!=null&&openingRects.Length>0
            ?new Rect(openingRects[0].x,openingRects[0].y,openingRects[0].width,openingRects[openingRects.Length-1].yMax-openingRects[0].y):(Rect?)null;
        public float OpeningTitleAlpha {
            get {
                var spec=Node?.OpeningTitle;
                if(spec==null||openingElapsed<0)return 0;
                float fadeIn=Math.Max(.01f,spec.FadeIn),hold=Math.Max(0,spec.Hold),fadeOut=Math.Max(.01f,spec.FadeOut);
                return Mathf.SmoothStep(0,1,Mathf.Clamp01(Math.Min(openingElapsed/fadeIn,(fadeIn+hold+fadeOut-openingElapsed)/fadeOut)));
            }
        }
        public void BeginOpeningTitle(){
            openingTitle="";openingLines.Clear();openingElapsed=-1;openingLastFrame=-1;
            var spec=Node?.OpeningTitle;
            if(spec==null||string.IsNullOrWhiteSpace(spec.Title))return;
            // Snapshot the entry environment once. Do not regenerate text or scan map things in OnGUI.
            var location=Find.WorldGrid.LongLatOf(Map.Tile);
            long ticks=Find.TickManager.TicksAbs;
            int minute=(int)(GenDate.DayTick(ticks,location.x)%2500*60L/2500);
            string time=GenDate.HourOfDay(ticks,location.x).ToString("00")+":"+minute.ToString("00");
            string date=GenDate.DateFullStringAt(ticks,location);
            string weather=Map.weatherManager.curWeather.LabelCap.ToString();
            string Expand(string text)=>RcText.T(text).Replace("{date}",date).Replace("{time}",time).Replace("{weather}",weather);
            openingTitle=Expand(spec.Title);
            foreach(var line in spec.Lines)openingLines.Add(Expand(line));
            openingElapsed=0;openingContents=null;openingWidth=-1;PrepareOpeningTitleColors();
        }
        void PrepareOpeningTitleColors(){
            openingHeadingColor=SpectrumBars.ColorFromHex(Node.OpeningTitle.TitleColor);
            openingLineColor=SpectrumBars.ColorFromHex(Node.OpeningTitle.TextColor);
        }
        void ExposeOpeningTitle(){
            Scribe_Values.Look(ref openingTitle,"rcOpeningTitle","");
            Scribe_Values.Look(ref openingElapsed,"rcOpeningTitleElapsed",-1);
            Scribe_Collections.Look(ref openingLines,"rcOpeningTitleLines",LookMode.Value);
            if(Scribe.mode==LoadSaveMode.PostLoadInit){
                openingLines=openingLines??new List<string>();openingLastFrame=-1;openingContents=null;openingWidth=-1;
                if(Node?.OpeningTitle!=null)PrepareOpeningTitleColors();
            }
        }
        void DrawOpeningTitle(){
            var spec=Node?.OpeningTitle;
            if(spec==null||openingElapsed<0||openingElapsed>=Math.Max(.01f,spec.FadeIn)+Math.Max(0,spec.Hold)+Math.Max(.01f,spec.FadeOut))return;
            if(Transitioning||EntryPending||black>0||Find.CurrentMap!=Map||Find.World.renderer.wantedMode!=WorldRenderMode.None){openingLastFrame=-1;return;}
            if(Event.current.type!=EventType.Repaint)return;
            float now=Time.realtimeSinceStartup;
            if(openingLastFrame>=0)openingElapsed+=Math.Min(.1f,Math.Max(0,now-openingLastFrame));
            openingLastFrame=now;
            float alpha=OpeningTitleAlpha;if(alpha<=0)return;
            // Cache styles; this purely visual overlay never captures input or changes game speed.
            if(openingHeadingStyle==null){
                openingHeadingStyle=new GUIStyle(Text.CurFontStyle){alignment=TextAnchor.UpperLeft,wordWrap=true,fontSize=34};
                openingLineStyle=new GUIStyle(Text.CurFontStyle){alignment=TextAnchor.UpperLeft,wordWrap=true,fontSize=21};
            }
            if(openingWidth!=UI.screenWidth||openingHeight!=UI.screenHeight||openingCinematic!=Cinematic)LayoutOpeningTitle();
            var previous=GUI.color;
            try{
                for(int i=0;i<openingContents.Length;i++)DrawOpeningLine(openingRects[i],openingContents[i],i==0?openingHeadingStyle:openingLineStyle,i==0?openingHeadingColor:openingLineColor,alpha);
            }finally{GUI.color=previous;}
        }
        void LayoutOpeningTitle(){
            openingWidth=UI.screenWidth;openingHeight=UI.screenHeight;openingCinematic=Cinematic;
            float scale=Math.Min(1,Math.Min(openingWidth/1280f,openingHeight/800f));
            openingHeadingStyle.fontSize=Math.Max(20,(int)(34*scale));openingLineStyle.fontSize=Math.Max(14,(int)(21*scale));
            if(openingContents==null){
                openingContents=new GUIContent[openingLines.Count+1];openingContents[0]=new GUIContent(openingTitle);
                for(int i=0;i<openingLines.Count;i++)openingContents[i+1]=new GUIContent(openingLines[i]);
                openingRects=new Rect[openingContents.Length];
            }
            float x=48*scale,width=Math.Min(720*scale,openingWidth-x*2),height=0;
            for(int i=0;i<openingContents.Length;i++){
                float h=(i==0?openingHeadingStyle:openingLineStyle).CalcHeight(openingContents[i],width)+(i==0?12:6)*scale;
                openingRects[i]=new Rect(x,height,width,h);height+=h;
            }
            // The vanilla inspect pane and its tabs use fixed UI pixels, not our text scale.
            // Reserve their full 200 + 30 pixel height plus spacing in a playable opening.
            float bottom=openingCinematic?64*scale:250;
            float y=Math.Max(100*scale,openingHeight-bottom-height);
            for(int i=0;i<openingRects.Length;i++)openingRects[i].y+=y;
        }
        static void DrawOpeningLine(Rect rect,GUIContent value,GUIStyle style,Color color,float alpha){
            var shadow=rect;shadow.x+=1;shadow.y+=2;
            GUI.color=new Color(0,0,0,.85f*alpha);GUI.Label(shadow,value,style);
            color.a*=alpha;GUI.color=color;GUI.Label(rect,value,style);
        }
    }
}
