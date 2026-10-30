using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public class ContentsWindow:Window
    {
        readonly Placement draft;readonly Action<Placement> apply;Vector2 scroll;string error="";
        readonly Dictionary<DeviceLoot,string> counts=new Dictionary<DeviceLoot,string>();
        public override Vector2 InitialSize=>new Vector2(760,600);
        public ContentsWindow(Placement value,Action<Placement> apply){draft=Json.Copy(value);draft.Contents=draft.Contents??new List<DeviceLoot>();this.apply=apply;doCloseX=true;forcePause=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect rect) {
            var host=DefDatabase<ThingDef>.GetNamed(draft.Def.Name);
            EditorUI.DrawLabel(new Rect(0,0,rect.width-35,32),host.LabelCap+RimCampaign.Runtime.RcText.T(" · 初始内容物"));
            EditorUI.DrawLabel(new Rect(0,34,rect.width,40),RimCampaign.Runtime.RcText.T("选择物品、数量和适用材质。应用时检查容量；内容物会随建筑生成并保存在存档中。"));
            Widgets.BeginScrollView(new Rect(0,80,rect.width,rect.height-210),ref scroll,new Rect(0,0,rect.width-20,draft.Contents.Count*45));
            DeviceLoot remove=null;
            for(int i=0;i<draft.Contents.Count;i++){
                var row=draft.Contents[i];float y=i*45;var item=DefDatabase<ThingDef>.GetNamedSilentFail(row.Def.Name);
                if(EditorUI.Button(new Rect(0,y,270,38),item?.LabelCap??RimCampaign.Runtime.RcText.T("选择物品")))PickItem(host,d=>{row.Def=Profiles.Ref(d);row.Stuff=d.MadeFromStuff?Profiles.Ref(GenStuff.DefaultStuffFor(d)):null;});
                if(!counts.TryGetValue(row,out var count))count=row.Count.ToString();
                count=Widgets.TextField(new Rect(280,y,85,38),count);counts[row]=count;if(int.TryParse(count,out int number))row.Count=number;
                if(EditorUI.Button(new Rect(375,y,200,38),item?.MadeFromStuff==true?(row.Stuff?.Name??RimCampaign.Runtime.RcText.T("默认材质")):RimCampaign.Runtime.RcText.T("无需材质"))&&item?.MadeFromStuff==true)
                    EditorUI.PickStuff(item,value=>row.Stuff=value);
                if(EditorUI.Button(new Rect(585,y,95,38),RimCampaign.Runtime.RcText.T("删除")))remove=row;
            }
            if(remove!=null)draft.Contents.Remove(remove);
            Widgets.EndScrollView();
            if(EditorUI.Button(new Rect(0,rect.height-122,160,34),RimCampaign.Runtime.RcText.T("+ 添加物品")))PickItem(host,d=>draft.Contents.Add(new DeviceLoot{Def=Profiles.Ref(d),Stuff=d.MadeFromStuff?Profiles.Ref(GenStuff.DefaultStuffFor(d)):null}));
            EditorUI.DrawLabel(new Rect(0,rect.height-82,rect.width,37),error);
            if(EditorUI.Button(new Rect(0,rect.height-40,rect.width,36),RimCampaign.Runtime.RcText.T("应用内容物"))){
                try{
                    if(draft.Contents.Any(row=>counts.TryGetValue(row,out var value)&&!int.TryParse(value,out _)))throw new ArgumentException(RimCampaign.Runtime.RcText.T("数量必须为整数。"));
                    PlacementStorage.Validate(draft);apply(draft);Close();
                }catch(Exception ex){error=ex.GetBaseException().Message;}
            }
        }
        static void PickItem(ThingDef host,Action<ThingDef> apply){
            var list=DefDatabase<ThingDef>.AllDefsListForReading.Where(d=>PlacementStorage.Allows(host,d)).ToList();
            EditorUI.Pick(list.Select(d=>new KeyValuePair<string,string>(d.defName,d.LabelCap+" ["+d.defName+"]")),id=>apply(list.Single(d=>d.defName==id)));
        }
    }
}
