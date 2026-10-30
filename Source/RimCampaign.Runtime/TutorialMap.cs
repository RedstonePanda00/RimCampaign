using System;
using System.Collections.Generic;
using HarmonyLib;
using RimCampaign.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Runtime {
    public sealed class TutorialStep {
        public string Target {get;set;}="";
        public string Text {get;set;}="";
    }
    // UI integrations publish actual drawn control bounds and successful actions, never duplicate their UI.
    public sealed class TutorialMapComponent : CustomMapComponent,ICampaignMapModule {
        public bool Enabled;
        public string Id="";
        public int Step;
        public List<TutorialStep> Steps=new List<TutorialStep>();
        public List<Act> OnComplete=new List<Act>();
        public Dictionary<string,string> Data=new Dictionary<string,string>();
        public bool Running {get{var c=CampaignController.ForMap(map);return Enabled&&Step<Steps.Count&&c?.Active==true&&c.Map==map;}}
        public string Target=>Running?Steps[Step].Target:"";
        Rect? highlight;
        Window highlightWindow;
        public Rect? TargetBounds=>highlight;
        float rectSeen;
        TimeSpeed previousSpeed;
        public static TutorialMapComponent Current=>LongEventHandler.ShouldWaitForEvent||Find.CurrentMap?.components==null?null:Find.CurrentMap.GetComponent<TutorialMapComponent>();
        public TutorialMapComponent(Map map):base(map){}
        public void Attach(CampaignController c,MapComponentSpec spec){Enabled=spec.Enabled;}
        public void SetEnabled(bool value){if(!value)Detach();Enabled=value;}
        public void Detach(){if(Running)Find.TickManager.CurTimeSpeed=previousSpeed;Enabled=false;highlight=null;}
        public void Begin(string id,List<TutorialStep> steps,List<Act> completion,Dictionary<string,string> data){
            if(!Enabled)throw new InvalidOperationException("Tutorial component is disabled.");
            if(Running)return;Id=id;Steps=steps;OnComplete=completion;Data=data;Step=0;highlight=null;
            previousSpeed=Find.TickManager.CurTimeSpeed;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
        }
        public void Report(string key,Rect local){
            if(!Running||key!=Target||Event.current.type!=EventType.Repaint)return;
            highlight=UI.GUIToScreenRect(local);highlightWindow=Find.WindowStack.currentlyDrawnWindow;rectSeen=Time.realtimeSinceStartup;
        }
        public void Satisfy(string key){
            if(!Running||key!=Target)return;
            Step++;highlight=null;
            if(!Running){Find.TickManager.CurTimeSpeed=previousSpeed;CampaignController.ForMap(map).QueueStory(OnComplete);}
        }
        public override void MapComponentUpdate(){if(Running)Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;}
        public override void ExposeData(){
            base.ExposeData();Scribe_Values.Look(ref Enabled,"enabled");Scribe_Values.Look(ref Id,"tutorialId","");Scribe_Values.Look(ref Step,"tutorialStep");Scribe_Values.Look(ref previousSpeed,"previousSpeed");
            string steps=Json.Write(Steps),actions=Json.Write(OnComplete),data=Json.Write(Data);
            ScribeJson.Look(ref steps,"steps");ScribeJson.Look(ref actions,"completion");ScribeJson.Look(ref data,"data");
            if(Scribe.mode==LoadSaveMode.LoadingVars){Steps=Json.Read<List<TutorialStep>>(steps);OnComplete=Json.Read<List<Act>>(actions);Data=Json.Read<Dictionary<string,string>>(data);highlight=null;}
        }
        public void FilterInput(){
            if(!Running)return;var e=Event.current;
            bool pointer=e.type==EventType.MouseDown||e.type==EventType.MouseUp||e.type==EventType.MouseDrag||e.type==EventType.ScrollWheel;
            bool key=e.type==EventType.KeyDown||e.type==EventType.KeyUp;
            if(key||pointer&&(!highlight.HasValue||Time.realtimeSinceStartup-rectSeen>.5f||!highlight.Value.Contains(UI.MousePositionOnUIInverted)||e.button!=0||e.type==EventType.ScrollWheel))e.Use();
        }
        public void DrawOverlay(Window window=null){
            if(!Running||Event.current.type!=EventType.Repaint)return;
            var old=GUI.color;var font=Text.Font;var anchor=Text.Anchor;int depth=GUI.depth;
            try{
                GUI.depth=-1000;
                Rect screen=window==null?new Rect(0,0,UI.screenWidth,UI.screenHeight):window.windowRect.AtZero();var hole=highlight??new Rect(0,0,0,0);
                if(window!=null)hole.position-=UI.GUIToScreenPoint(Vector2.zero);
                var tint=new Color(0,0,0,.66f);
                if(highlight.HasValue&&Time.realtimeSinceStartup-rectSeen<.5f){
                    hole=hole.ExpandedBy(5);hole=Rect.MinMaxRect(Mathf.Clamp(hole.xMin,0,screen.width),Mathf.Clamp(hole.yMin,0,screen.height),Mathf.Clamp(hole.xMax,0,screen.width),Mathf.Clamp(hole.yMax,0,screen.height));
                    Widgets.DrawBoxSolid(new Rect(0,0,screen.width,hole.y),tint);Widgets.DrawBoxSolid(new Rect(0,hole.yMax,screen.width,Math.Max(0,screen.height-hole.yMax)),tint);
                    Widgets.DrawBoxSolid(new Rect(0,hole.y,hole.x,hole.height),tint);Widgets.DrawBoxSolid(new Rect(hole.xMax,hole.y,Math.Max(0,screen.width-hole.xMax),hole.height),tint);
                    GUI.color=new Color(1,.82f,.25f,.65f+.35f*Mathf.Sin(Time.realtimeSinceStartup*5));Widgets.DrawBox(hole,3);
                }else Widgets.DrawBoxSolid(screen,tint);
                if(window!=highlightWindow)return;
                Text.Font=GameFont.Medium;Text.Anchor=TextAnchor.MiddleCenter;GUI.color=Color.white;
                float y=hole.center.y<screen.height*.5f?screen.height-100:36;
                Widgets.Label(new Rect(24,y,screen.width-48,64),RcText.T(Steps[Step].Text));
            }finally{GUI.depth=depth;GUI.color=old;Text.Font=font;Text.Anchor=anchor;}
        }
    }
    [HarmonyPatch(typeof(UIRoot_Play),nameof(UIRoot_Play.UIRootOnGUI))]
    static class TutorialInterface {
        static void Prefix()=>TutorialMapComponent.Current?.FilterInput();
        static void Postfix()=>TutorialMapComponent.Current?.DrawOverlay();
    }
    // Waylith defers GUI.Window drawing. Mask and filter its actual callback as well as the map UI.
    [HarmonyPatch(typeof(Window),"InnerWindowOnGUI")]
    static class TutorialWindowInterface {
        static void Prefix()=>TutorialMapComponent.Current?.FilterInput();
        static void Postfix(Window __instance)=>TutorialMapComponent.Current?.DrawOverlay(__instance);
    }
    [HarmonyPatch(typeof(TickManager),nameof(TickManager.DoSingleTick))]
    static class TutorialPause {static bool Prefix()=>TutorialMapComponent.Current?.Running!=true;}
}
