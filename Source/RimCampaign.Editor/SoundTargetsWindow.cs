using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public sealed class SoundTargetsWindow:Window
    {
        readonly List<KeyValuePair<string,string>> choices;readonly List<string> selected;Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(560,600);
        public SoundTargetsWindow(List<KeyValuePair<string,string>> choices,List<string> selected){this.choices=choices;this.selected=selected;doCloseX=true;forcePause=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect r){
            EditorUI.DrawLabel(new Rect(0,0,r.width,42),RimCampaign.Runtime.RcText.T("勾选声音接收目标；未勾选任何目标时，由类型、分组和阵营筛选。\n仍需位于有效声音范围内。"));
            Widgets.BeginScrollView(new Rect(0,50,r.width,r.height-100),ref scroll,new Rect(0,0,r.width-20,choices.Count*34));
            for(int i=0;i<choices.Count;i++){var choice=choices[i];bool before=selected.Contains(choice.Key),value=before;Widgets.CheckboxLabeled(new Rect(0,i*34,r.width-30,30),choice.Value,ref value);if(value&&!before)selected.Add(choice.Key);if(before&&!value)selected.Remove(choice.Key);}
            Widgets.EndScrollView();if(EditorUI.Button(new Rect(0,r.height-40,r.width,36),RimCampaign.Runtime.RcText.T("返回")))Close();
        }
    }
}
