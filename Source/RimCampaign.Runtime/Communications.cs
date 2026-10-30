using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimCampaign.Runtime
{
    public class CommunicationPortraitDef : Def
    {
        public string texturePath;
        public float cropX, cropY, cropWidth=1, cropHeight=1;
        public override IEnumerable<string> ConfigErrors() {
            foreach(var error in base.ConfigErrors())yield return error;
            if(string.IsNullOrWhiteSpace(texturePath))yield return "Communication portrait needs a texturePath.";
            if(!CommunicationSpec.ValidCrop(cropX,cropY,cropWidth,cropHeight))yield return "Invalid portrait crop.";
        }
    }
    public sealed class CommunicationPresenter
    {
        readonly Dictionary<string,ICommunicationRenderer> renderers=new Dictionary<string,ICommunicationRenderer>();
        readonly HashSet<string> failed=new HashSet<string>();
        SpectrumSignal spectrum;
        string spectrumKey;
        float spectrumStarted,spectrumUpdated;
        double spectrumElapsed;
        public void Draw(CommunicationMessage message,Pawn speaker,Rect viewport)=>Draw(message,speaker,viewport,null);
        public void Draw(CommunicationMessage message,Pawn speaker,Rect viewport,Rect? avoid) {
            if(message==null)return;
            var color=GUI.color;var font=Text.Font;var anchor=Text.Anchor;var wrap=Text.WordWrap;
            try {
                GUI.color=Color.white;Text.Font=GameFont.Small;Text.Anchor=TextAnchor.UpperLeft;Text.WordWrap=true;
                var s=message.Spec;
                float width=Math.Min(s.Width,viewport.width-16),size=Math.Min(s.PortraitSize,width/3);
                float bodyWidth=width-size-24;
                float bodyHeight=Text.CalcHeight(RcText.T(message.Text),bodyWidth);
                var spectrumSpec=s.Spectrum;
                bool showSpectrum=spectrumSpec?.Enabled==true;
                float height=Math.Max(size,bodyHeight+54+(showSpectrum?spectrumSpec.Height+12:0));
                var panel=new Rect(viewport.x+Mathf.Clamp(s.X,0,Math.Max(0,viewport.width-width)),
                    viewport.y+Mathf.Clamp(s.Y,0,Math.Max(0,viewport.height-height)),width,height);
                if(avoid.HasValue&&panel.Overlaps(avoid.Value))panel.y=Math.Max(viewport.y,avoid.Value.y-height-20);
                var portrait=new Rect(panel.x,panel.y,size,size);
                var textPanel=new Rect(portrait.xMax,panel.y,width-size,height);
                var context=new CommunicationDrawContext {
                    Panel=panel,PortraitRect=portrait,TextPanel=textPanel,BackgroundOpacity=s.BackgroundOpacity,
                    NameRect=new Rect(textPanel.x+12,panel.y+8,bodyWidth,26),
                    TextRect=new Rect(textPanel.x+12,panel.y+36,bodyWidth,bodyHeight+8),Speaker=speaker,
                    SpectrumRect=showSpectrum?new Rect(textPanel.x+12,panel.yMax-spectrumSpec.Height-12,Math.Min(bodyWidth,spectrumSpec.Width),spectrumSpec.Height):default(Rect),
                    Name=string.IsNullOrWhiteSpace(s.SpeakerName)?speaker?.LabelShortCap.ToString()??RimCampaign.Runtime.RcText.T("通讯"):RcText.T(s.SpeakerName),
                    Text=RcText.T(message.Text),Elapsed=message.Elapsed,Duration=s.Duration,Data=s.RendererData
                };
                ResolvePortrait(s,speaker,context);
                context.DrawDefaultPortrait=()=>DrawPortrait(context);
                context.DrawDefaultText=()=>DrawText(context);
                context.DrawDefaultSpectrum=()=>{if(showSpectrum)DrawSpectrum(message,context);};
                context.DrawDefaultContents=()=>{DrawPortrait(context);DrawText(context);context.DrawDefaultSpectrum();};
                if(!string.IsNullOrEmpty(s.Renderer)&&!failed.Contains(s.Renderer)) {
                    try {
                        if(!renderers.TryGetValue(s.Renderer,out var renderer)) {
                            renderer=Modules.Create(s.Renderer) as ICommunicationRenderer;
                            if(renderer==null)throw new InvalidOperationException("Missing ICommunicationRenderer: "+s.Renderer);
                            renderers[s.Renderer]=renderer;
                        }
                        renderer.Draw(context);return;
                    } catch(Exception ex) {
                        failed.Add(s.Renderer);Log.Warning("[RimCampaign] Communication renderer failed; using default: "+ex);
                        GUI.color=Color.white;Text.Font=GameFont.Small;Text.Anchor=TextAnchor.UpperLeft;Text.WordWrap=true;
                    }
                }
                DrawDefault(context);
            } finally {GUI.color=color;Text.Font=font;Text.Anchor=anchor;Text.WordWrap=wrap;}
        }
        void DrawSpectrum(CommunicationMessage message,CommunicationDrawContext context)
        {
            if(Event.current.type!=EventType.Repaint)return;
            var spec=message.Spec.Spectrum;
            string key=message.ConversationId+"|"+message.ActionId+"|"+context.Name+"|"+message.Text;
            float now=Time.realtimeSinceStartup;
            if(spectrum==null||spectrum.Levels.Count!=spec.BarCount||spectrumKey!=key||message.Elapsed<spectrumElapsed){
                spectrum=new SpectrumSignal(spec.BarCount);spectrumKey=key;spectrumStarted=now;spectrumUpdated=now-.016f;
            }
            spectrumElapsed=message.Elapsed;
            spectrum.Update(now-spectrumStarted,now-spectrumUpdated,spec.SimulateSpeech);
            spectrumUpdated=now;
            SpectrumBars.Draw(context.SpectrumRect,spectrum.Levels,SpectrumBars.ColorFromHex(spec.Color));
        }
        public static void ResolvePortrait(CommunicationSpec spec,Pawn pawn,CommunicationDrawContext result) {
            string path=spec.PortraitPath;float x=spec.CropX,y=spec.CropY,w=spec.CropWidth,h=spec.CropHeight;
            if(!string.IsNullOrEmpty(spec.Portrait?.Name)) {
                var preset=DefDatabase<CommunicationPortraitDef>.GetNamedSilentFail(spec.Portrait.Name);
                if(preset!=null){path=preset.texturePath;x=preset.cropX;y=preset.cropY;w=preset.cropWidth;h=preset.cropHeight;}
            }
            result.PortraitUV=new Rect(0,0,1,1);
            result.Portrait=string.IsNullOrEmpty(path)?null:ContentFinder<Texture2D>.Get(path,false);
            if(result.Portrait!=null) {
                if(CommunicationSpec.ValidCrop(x,y,w,h))result.PortraitUV=new Rect(x,1-y-h,w,h);
            } else if(pawn!=null) {
                // PortraitsCache uses the Pawn's render tree, including apparel and mod render nodes.
                result.Portrait=PortraitsCache.Get(pawn,new Vector2(result.PortraitRect.width,result.PortraitRect.height),Rot4.South);
            }
        }
        public static void DrawDefault(CommunicationDrawContext c) {
            // Independent touching panels, with no parent background or enclosing frame.
            Widgets.DrawBoxSolid(c.TextPanel,new Color(.055f,.075f,.095f,c.BackgroundOpacity));
            c.DrawDefaultContents();
        }
        public static void DrawPortrait(CommunicationDrawContext c) {
            Widgets.DrawBoxSolid(c.PortraitRect,new Color(.055f,.075f,.095f,c.BackgroundOpacity));
            if(c.Portrait!=null)GUI.DrawTextureWithTexCoords(c.PortraitRect,c.Portrait,c.PortraitUV);
            else {Text.Anchor=TextAnchor.MiddleCenter;Widgets.Label(c.PortraitRect,"?");Text.Anchor=TextAnchor.UpperLeft;}
        }
        public static void DrawText(CommunicationDrawContext c) {
            GUI.color=c.Data!=null&&c.Data.TryGetValue("side",out var side)&&side=="enemy"?new Color(1,.32f,.3f):new Color(.4f,.72f,1);Widgets.Label(c.NameRect,c.Name);GUI.color=Color.white;
            Widgets.Label(c.TextRect,c.Text);
        }
    }
    public partial class CampaignController : IConversationHost
    {
        public bool ExecuteConversation(List<Act> lines,string source,double delta)=>Communications.ExecuteConversation(lines,source,Freeze?Math.Max(delta,communicationPresentationDelta):delta);
        public CommunicationQueue Communications=new CommunicationQueue();
        double communicationPresentationDelta;
        readonly CommunicationPresenter communicationPresenter=new CommunicationPresenter();
        void PruneCommunications()=>Communications.Retain(State.Fibers.SelectMany(ActiveActions).Where(a=>a.Kind=="Communication").Select(a=>a.Id));
        void ExposeCommunications() {
            string data=Json.Write(Communications);ScribeJson.Look(ref data,"rcCommunications");
            if(Scribe.mode==LoadSaveMode.LoadingVars)Communications=data==null?new CommunicationQueue():Json.Read<CommunicationQueue>(data);
        }
        void DrawCommunications() {
            var message=Communications.Current;if(message==null)return;
            if(Event.current.type==EventType.Repaint&&Communications.TryNotifyConversationStart())CommunicationAudio.PlayStartCue();
            string id=message.Spec.Speaker=="$source"?message.Source:message.Spec.Speaker;
            var pawn=string.IsNullOrEmpty(id)?null:ResolveTriggerPawn(id);
            communicationPresenter.Draw(message,pawn,new Rect(0,0,UI.screenWidth,UI.screenHeight),OpeningTitleBounds);
        }
    }
}
