using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimCampaign.SDK
{
    // Register using CampaignModule(id, "CommunicationRenderer", displayName).
    // Rendering must not advance the timeline; IMGUI can draw several times per frame.
    public interface ICommunicationRenderer { void Draw(CommunicationDrawContext context); }
    // Shader-only integrations can subclass this instead of reimplementing layout or text.
    public abstract class CommunicationMaterialRenderer : ICommunicationRenderer
    {
        protected abstract Material PortraitMaterial(CommunicationDrawContext context);
        protected virtual void DrawFrame(CommunicationDrawContext context) {
            var color=new Color(.055f,.075f,.095f,context.BackgroundOpacity);
            Widgets.DrawBoxSolid(context.PortraitRect,color);
            Widgets.DrawBoxSolid(context.TextPanel,color);
        }
        public virtual void Draw(CommunicationDrawContext context) {
            DrawFrame(context);
            if(context.Portrait==null)context.DrawDefaultPortrait();
            else GenUI.DrawTextureWithMaterial(context.PortraitRect,context.Portrait,PortraitMaterial(context),context.PortraitUV);
            context.DrawDefaultText();context.DrawDefaultSpectrum();
        }
    }
    public sealed class CommunicationDrawContext
    {
        // Panel is only the overall layout bounds. PortraitRect and TextPanel are adjacent peers.
        public Rect Panel, PortraitRect, TextPanel, NameRect, TextRect, PortraitUV, SpectrumRect;
        public float BackgroundOpacity;
        public Texture Portrait;
        public Pawn Speaker;
        public string Name, Text;
        public double Elapsed, Duration;
        public IReadOnlyDictionary<string,string> Data;
        // Plugins may draw a shader frame, then reuse either or both of these delegates.
        public Action DrawDefaultPortrait, DrawDefaultText, DrawDefaultContents;
        public Action DrawDefaultSpectrum;
    }
}
