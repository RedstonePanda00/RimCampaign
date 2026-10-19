using System;
using System.Linq;
using RimCampaign.Core;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public sealed class MapFactionsWindow : Window
    {
        readonly MapSpec draft;readonly Action<MapSpec> apply;Vector2 scroll;string error="";
        public override Vector2 InitialSize=>new Vector2(720,600);
        public MapFactionsWindow(MapSpec map,Action<MapSpec> apply){draft=Json.Copy(map);this.apply=apply;doCloseX=true;absorbInputAroundWindow=true;forcePause=true;}
        static string Label(string kind)=>kind=="Ally"?"盟友":kind=="Hostile"?"敌人":"中立";
        static Color Tint(string kind)=>kind=="Ally"?new Color(.35f,.9f,.45f):kind=="Hostile"?new Color(1f,.4f,.35f):Color.white;
        public override void DoWindowContents(Rect r) {
            Widgets.Label(new Rect(0,0,r.width-40,32),draft.Name+" · 地图阵营");
            Widgets.Label(new Rect(0,36,r.width,56),"玩家与盟友同队，所有敌人同队，两队互相敌对。\n中立阵营各自独立，与所有其他阵营默认中立。");
            if(Widgets.ButtonText(new Rect(0,100,180,34),"+ 添加阵营"))draft.Factions.Add(new MapFaction{Name="阵营 "+(draft.Factions.Count+1)});
            var area=new Rect(0,146,r.width,r.height-220);
            Widgets.BeginScrollView(area,ref scroll,new Rect(0,0,area.width-20,draft.Factions.Count*58));
            for(int i=0;i<draft.Factions.Count;i++) {
                var faction=draft.Factions[i];float y=i*58;
                faction.Name=Widgets.TextField(new Rect(0,y,area.width-200,38),faction.Name);
                var choice=new Rect(area.width-185,y,160,38);
                if(faction.Id=="Player"){Widgets.Label(choice,"玩家（盟友队）");continue;}
                var old=GUI.color;GUI.color=Tint(faction.Side);
                if(Widgets.ButtonText(choice,Label(faction.Side))) {
                    string id=faction.Id;
                    EditorUI.Pick(new[]{"Ally","Hostile","Neutral"}.Select(k=>new System.Collections.Generic.KeyValuePair<string,string>(k,Label(k))),k=>MapFactions.SetSide(draft,id,k));
                }
                GUI.color=old;
            }
            Widgets.EndScrollView();
            Widgets.Label(new Rect(0,r.height-68,r.width,28),error);
            if(Widgets.ButtonText(new Rect(0,r.height-38,r.width,36),"应用阵营配置")){
                if(draft.Factions.Any(f=>string.IsNullOrWhiteSpace(f.Name)))error="阵营名称不能为空。";
                else{apply(draft);Close();}
            }
        }
    }
}
