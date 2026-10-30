using System;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public sealed class MapComponentsWindow : Window
    {
        readonly MapSpec draft;readonly Action<MapSpec> save;readonly CampaignController live;readonly Project project;
        Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(780,480);
        public MapComponentsWindow(Project project,MapSpec map,Action<MapSpec> save,CampaignController live=null){this.project=project;draft=Json.Copy(map);this.save=save;this.live=live;forcePause=true;doCloseX=true;absorbInputAroundWindow=true;}
        void Commit(){if(live!=null)live.MapSpec.Components=Json.Copy(draft.Components);else save(draft);}
        public override void DoWindowContents(Rect r){
            EditorUI.DrawLabel(new Rect(0,0,r.width,48),live==null?RimCampaign.Runtime.RcText.T("本图组件（默认不挂载）。启用状态与是否挂载分别配置。"):RimCampaign.Runtime.RcText.T("热测试中的实时组件：添加／移除会立即执行生命周期回调；不会改写创作工程。"));
            if(EditorUI.Button(new Rect(0,50,180,32),RimCampaign.Runtime.RcText.T("添加地图组件…")))EditorUI.Pick(CampaignMapModules.Available.Where(t=>draft.Components.All(s=>s.Type!=t.FullName)).Select(t=>new System.Collections.Generic.KeyValuePair<string,string>(t.FullName,t.Name)),name=>{
                try{var spec=new MapComponentSpec{Type=name,Name=name.Split('.').Last()};if(live!=null)CampaignMapModules.Add(live,spec);draft.Components.Add(spec);Commit();}catch(Exception ex){EditorUI.Error(ex);}
            });
            var area=new Rect(0,90,r.width,r.height-100);Widgets.BeginScrollView(area,ref scroll,new Rect(0,0,area.width-20,draft.Components.Count*78));
            for(int i=0;i<draft.Components.Count;i++){
                var spec=draft.Components[i];float y=i*78;
                EditorUI.DrawLabel(new Rect(0,y,460,25),spec.Name);EditorUI.DrawLabel(new Rect(0,y+26,460,38),spec.Type);
                if(EditorUI.Button(new Rect(470,y,80,30),spec.Enabled?RimCampaign.Runtime.RcText.T("启用 ✓"):RimCampaign.Runtime.RcText.T("已停用")))try{if(live!=null)CampaignMapModules.Enable(live,spec.Type,!spec.Enabled);spec.Enabled=!spec.Enabled;Commit();}catch(Exception ex){EditorUI.Error(ex);}
                if(EditorUI.Button(new Rect(555,y,80,30),RimCampaign.Runtime.RcText.T("设置")))Find.WindowStack.Add(new ObjectWindow(spec,value=>{var next=(MapComponentSpec)value;if(next.Type!=spec.Type)throw new InvalidOperationException(RimCampaign.Runtime.RcText.T("替换类型请先移除再添加。"));if(live!=null)throw new InvalidOperationException(RimCampaign.Runtime.RcText.T("组件自定义设置请在创作模式编辑后重新热测试。"));draft.Components[draft.Components.FindIndex(s=>s.Id==spec.Id)]=next;Commit();},project,draft));
                if(EditorUI.Button(new Rect(640,y,75,30),RimCampaign.Runtime.RcText.T("移除"))){try{if(live!=null)CampaignMapModules.Remove(live,spec.Type);draft.Components.Remove(spec);Commit();}catch(Exception ex){EditorUI.Error(ex);}break;}
            }
            Widgets.EndScrollView();
        }
    }
}
