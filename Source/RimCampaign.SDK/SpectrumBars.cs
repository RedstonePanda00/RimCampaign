using System.Collections.Generic;
using RimCampaign.Core;
using UnityEngine;
using Verse;

namespace RimCampaign.SDK
{
    // Framework-owned drawing primitive: no faction assets or mod dependencies.
    public static class SpectrumBars
    {
        public static Color ColorFromHex(string value)
        {
            if(!CommunicationSpectrumSpec.TryColor(value,out var rgba))return Color.white;
            return new Color((rgba>>24)/255f,((rgba>>16)&255)/255f,((rgba>>8)&255)/255f,(rgba&255)/255f);
        }
        public static void Draw(Rect rect,IReadOnlyList<float> levels,Color color)
        {
            if(Event.current.type!=EventType.Repaint||levels==null||levels.Count==0||rect.width<=0||rect.height<=0)return;
            float stride=rect.width/levels.Count,gap=Mathf.Min(2,stride*.32f),width=stride-gap;
            for(int i=0;i<levels.Count;i++){
                float value=levels[i];if(float.IsNaN(value)||float.IsInfinity(value))value=0;
                float height=Mathf.Max(1,Mathf.Clamp01(value)*rect.height);
                Widgets.DrawBoxSolid(new Rect(rect.x+i*stride,rect.center.y-height*.5f,width,height),color);
            }
        }
    }
}
