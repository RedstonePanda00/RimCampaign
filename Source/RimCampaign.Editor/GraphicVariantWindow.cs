using System;
using RimCampaign.Core;
using RimCampaign.Runtime;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public sealed class GraphicVariantWindow : Window
    {
        readonly Placement placement;readonly bool brush;readonly Action<int?> select;Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(650,570);
        public GraphicVariantWindow(Placement placement,bool brush,Action<int?> select){
            this.placement=placement;this.brush=brush;this.select=select;doCloseX=true;absorbInputAroundWindow=true;
        }
        void Choose(int? index){EditorFocus.Release();select(index);Close();EditorFocus.Release();}
        public override void DoWindowContents(Rect rect){
            var def=DefDatabase<ThingDef>.GetNamedSilentFail(placement.Def?.Name??"");
            var variants=PlacementGraphics.Variants(def);
            if(variants==null){EditorUI.DrawLabel(rect,RimCampaign.Runtime.RcText.T("此对象没有可选随机款式。"));return;}
            EditorUI.DrawLabel(new Rect(0,0,rect.width-30,35),def.LabelCap+RimCampaign.Runtime.RcText.T("：固定贴图款式"));
            if(EditorUI.Button(new Rect(0,40,rect.width,34),brush?RimCampaign.Runtime.RcText.T("每次放置随机选定并固定"):RimCampaign.Runtime.RcText.T("随机选择一款并固定"))){
                Choose(brush?(int?)null:Rand.Range(0,variants.SubGraphicsCount));return;
            }
            int columns=4;float width=(rect.width-24)/columns;
            Widgets.BeginScrollView(new Rect(0,85,rect.width,rect.height-90),ref scroll,
                new Rect(0,0,rect.width-20,((variants.SubGraphicsCount+columns-1)/columns)*125));
            for(int i=0;i<variants.SubGraphicsCount;i++){
                var cell=new Rect(i%columns*width,i/columns*125,width-6,118);
                Widgets.DrawMenuSection(cell);
                var image=new Rect(cell.x+12,cell.y+5,cell.width-24,84);
                var color=GUI.color;
                try{
                    GUI.color=def.MadeFromStuff?def.GetColorForStuff(PlacementStorage.StuffFor(def,placement.Stuff)):Color.white;
                    GUI.DrawTexture(image,variants.SubGraphicAtIndex(i).MatSingle.mainTexture,ScaleMode.ScaleToFit);
                }finally{GUI.color=color;}
                EditorUI.DrawLabel(new Rect(cell.x+6,cell.y+91,cell.width-12,24),RimCampaign.Runtime.RcText.T("款式 #")+(i+1)+(PlacementGraphics.Index(placement,variants.SubGraphicsCount)==i?" ✓":""));
                if(Widgets.ButtonInvisible(cell)){Widgets.EndScrollView();Choose(i);return;}
            }
            Widgets.EndScrollView();
        }
    }
}
