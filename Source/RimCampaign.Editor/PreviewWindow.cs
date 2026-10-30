using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimCampaign.Editor
{
    public class PreviewWindow : Window,IHost
    {
        readonly CommunicationQueue communications=new CommunicationQueue();
        readonly CommunicationPresenter presenter=new CommunicationPresenter();
        readonly MapSpec map;readonly Engine engine;readonly RunState state=new RunState();
        string subtitle="",unsupported="";float black,clock;Dictionary<string,float> progress=new Dictionary<string,float>();
        Dictionary<string,Cell> positions=new Dictionary<string,Cell>();bool play;
        public override Vector2 InitialSize=>new Vector2(960,720);
        public PreviewWindow(MapSpec source){
            map=Json.Copy(source);map.Rules.Clear();map.Objectives.Clear();
            engine=new Engine(map,state,this);doCloseX=true;absorbInputAroundWindow=true;forcePause=true;
            foreach(var p in map.Objects)positions[p.Id]=Json.Copy(p.Position);
        }
        public override void WindowUpdate(){base.WindowUpdate();if(play){clock+=Time.unscaledDeltaTime;engine.Tick(0,Time.unscaledDeltaTime);}}
        public double Read(string key)=>42;
        public bool Execute(Act a,string source,double dt){
            float value=progress.TryGetValue(a.Id,out var n)?n:0;value+=(float)dt;progress[a.Id]=value;
            float f=Mathf.Clamp01(value/(float)Math.Max(.01,a.Value));
            if(a.Kind=="Communication")return communications.Execute(a,source,dt);
            if(a.Kind=="Subtitle"){subtitle=a.Text;return f>=1;}
            if(a.Kind=="Fade"){black=a.Text=="in"?1-f:f;return f>=1;}
            if(a.Kind=="Sound"){DefDatabase<SoundDef>.GetNamedSilentFail(a.Target)?.PlayOneShotOnCamera();return true;}
            if(a.Kind=="Dialogue"){subtitle=a.Text;if(value>(a.Value>0?a.Value:3))return true;return false;}
            if(a.Kind=="Move"){var actor=map.Objects.FirstOrDefault(p=>p.Role==a.Target);var marker=map.Objects.FirstOrDefault(p=>p.Id==a.Text);if(actor!=null&&marker!=null)positions[actor.Id]=marker.Position;return f>=1;}
            if(a.Kind=="Camera")return f>=1;
            unsupported=RimCampaign.Runtime.RcText.T("预览占位：")+a.Kind+RimCampaign.Runtime.RcText.T("（实际效果请导出后测试）");return true;
        }
        public override void DoWindowContents(Rect r){
            if(EditorUI.Button(new Rect(0,0,160,32),play?RimCampaign.Runtime.RcText.T("暂停预览"):RimCampaign.Runtime.RcText.T("播放入场演出"))){
                if(!play&&clock==0){foreach(var a in Validation.Actions(map.Entry))a.Freeze=true;engine.Enqueue(map.Entry);}
                play=!play;
            }
            if(EditorUI.Button(new Rect(170,0,160,32),RimCampaign.Runtime.RcText.T("播放失败演出"))){foreach(var a in Validation.Actions(map.Defeat))a.Freeze=true;engine.Enqueue(map.Defeat);play=true;}
            EditorUI.DrawLabel(new Rect(340,0,r.width-350,35),RimCampaign.Runtime.RcText.T("仅演出预览；HUD 使用示例值 42"));
            var view=new Rect(0,45,r.width,r.height-120);Widgets.DrawBoxSolid(view,new Color(.15f,.19f,.13f));
            float scale=Math.Min(view.width/map.Width,view.height/map.Height);
            foreach(var p in map.Objects){
                var position=positions[p.Id];var rect=new Rect(view.x+position.X*scale,view.y+(map.Height-1-position.Z)*scale,scale,scale);
                var def=DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name);
                if(p.Kind=="Thing"&&def!=null)Widgets.DefIcon(rect,def);else Widgets.DrawBoxSolid(rect,p.Kind=="Pawn"?Color.cyan:Color.gray);
            }
            foreach(var hud in map.Hud){var rect=new Rect(hud.X,hud.Y,hud.Width,hud.Height);
                if(hud.Kind=="Progress")Widgets.FillableBar(rect,.42f);else EditorUI.DrawLabel(rect,hud.Text+(hud.Kind=="Text"?"":" 42"));
            }
            if(black>0)Widgets.DrawBoxSolid(view,new Color(0,0,0,black));
            presenter.Draw(communications.Current,null,view);
            EditorUI.DrawLabel(new Rect(20,r.height-115,r.width-40,65),subtitle);
            EditorUI.DrawLabel(new Rect(0,r.height-40,r.width,35),unsupported);
        }
    }
}
