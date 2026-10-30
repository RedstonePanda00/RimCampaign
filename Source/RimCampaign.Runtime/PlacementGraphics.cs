using System;
using RimCampaign.Core;
using Verse;

namespace RimCampaign.Runtime
{
    public static class PlacementGraphics
    {
        public static Graphic_Random Variants(ThingDef def)=>def?.graphicData?.Graphic as Graphic_Random;
        public static int Index(Placement placement,int count){
            if(count<=0)return 0;
            if(placement.GraphicIndex.HasValue)return placement.GraphicIndex.Value;
            // Unlike string.GetHashCode, this fallback is stable between game processes.
            uint hash=2166136261;
            foreach(char ch in placement.Id??""){hash^=ch;hash=unchecked(hash*16777619);}
            return (int)(hash%(uint)count);
        }
        public static void FixNewPlacement(Placement p,ThingDef def){
            var variants=Variants(def);
            if(variants!=null)p.GraphicIndex=Index(p,variants.SubGraphicsCount);
        }
        public static void Validate(Placement p,ThingDef def){
            if(!p.GraphicIndex.HasValue)return;
            var variants=Variants(def);
            if(variants==null||p.GraphicIndex<0||p.GraphicIndex>=variants.SubGraphicsCount)
                throw new InvalidOperationException(def.LabelCap+RimCampaign.Runtime.RcText.T(" 的贴图款式已失效，请重新选择款式。"));
        }
        public static Graphic Preview(Placement p,ThingDef def){
            var graphic=def?.graphicData?.Graphic;
            if(graphic is Graphic_Random variants)
                return variants.SubGraphicAtIndex(Math.Max(0,Index(p,variants.SubGraphicsCount))%variants.SubGraphicsCount);
            return graphic;
        }
        public static void Apply(Placement p,Thing thing){
            var variants=Variants(thing.def);
            if(variants==null)return;
            Validate(p,thing.def);
            thing.overrideGraphicIndex=Index(p,variants.SubGraphicsCount);
        }
    }
}
