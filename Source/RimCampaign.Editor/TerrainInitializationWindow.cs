using System;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public sealed class TerrainInitializationWindow:Window
    {
        readonly TerrainInitialization settings;readonly Action<TerrainInitialization> apply;string error="";
        public override Vector2 InitialSize=>new Vector2(650,605);
        public TerrainInitializationWindow(TerrainInitialization previous,Action<TerrainInitialization> apply) {
            settings=previous==null?new TerrainInitialization():Json.Copy(previous);this.apply=apply;
            doCloseX=true;absorbInputAroundWindow=true;forcePause=true;
        }
        void Pick(bool secondary) {
            var options=DefDatabase<BiomeDef>.AllDefs.Where(b=>(b.terrainsByFertility.Count>0||b.inVacuum)&&(!b.inVacuum||ModsConfig.OdysseyActive)&&(!secondary||!b.inVacuum)).OrderBy(b=>b.LabelCap.ToString());
            Find.WindowStack.Add(new FloatMenu(options.Select(b=>new FloatMenuOption(b.LabelCap,()=>{
                if(secondary)settings.SecondaryBiome=Profiles.Ref(b);
                else {settings.Biome=Profiles.Ref(b);if(b.inVacuum)settings.SecondaryBiome=null;settings.Temperature=b.constantOutdoorTemperature??(b.defName=="BorealForest"?5:b.defName.Contains("Desert")?30:21);}
            })).ToList()));
        }
        public override void DoWindowContents(Rect r) {
            EditorUI.DrawLabel(new Rect(0,0,r.width-30,30),RimCampaign.Runtime.RcText.T("地形初始化"));
            EditorUI.DrawLabel(new Rect(0,38,135,30),RimCampaign.Runtime.RcText.T("主生态"));
            if(EditorUI.Button(new Rect(140,38,r.width-140,30),TerrainInitializer.Resolve(settings.Biome).LabelCap))Pick(false);
            bool mixed=settings.SecondaryBiome!=null;
            bool before=mixed;Widgets.CheckboxLabeled(new Rect(0,82,r.width,30),RimCampaign.Runtime.RcText.T("混合生态（需要 Odyssey，按格保存生态边界）"),ref mixed,!ModsConfig.OdysseyActive||TerrainInitializer.Resolve(settings.Biome).inVacuum);
            if(before!=mixed)settings.SecondaryBiome=mixed?Profiles.Ref(DefDatabase<BiomeDef>.GetNamed("Desert")):null;
            if(mixed){
                if(EditorUI.Button(new Rect(140,122,r.width-140,30),TerrainInitializer.Resolve(settings.SecondaryBiome).LabelCap))Pick(true);
                EditorUI.DrawLabel(new Rect(0,122,135,30),RimCampaign.Runtime.RcText.T("副生态"));
                EditorUI.DrawLabel(new Rect(0,162,180,28),RimCampaign.Runtime.RcText.T("副生态占比 ")+settings.SecondaryFraction.ToString("P0"));
                settings.SecondaryFraction=Widgets.HorizontalSlider(new Rect(200,166,r.width-200,24),settings.SecondaryFraction,.1f,.9f);
            }
            EditorUI.DrawLabel(new Rect(0,202,135,30),RimCampaign.Runtime.RcText.T("生成种子"));settings.Seed=Widgets.TextField(new Rect(140,202,r.width-140,30),settings.Seed);
            EditorUI.DrawLabel(new Rect(0,242,200,30),RimCampaign.Runtime.RcText.T("地形斑块尺寸 ")+settings.PatchSize.ToString("F0"));
            settings.PatchSize=Widgets.HorizontalSlider(new Rect(220,246,r.width-220,24),settings.PatchSize,4,100);
            EditorUI.DrawLabel(new Rect(0,282,200,30),RimCampaign.Runtime.RcText.T("地表室外温度 ")+settings.Temperature.ToString("F0")+" °C");
            if(!TerrainInitializer.Resolve(settings.Biome).inVacuum)settings.Temperature=Widgets.HorizontalSlider(new Rect(220,286,r.width-220,24),settings.Temperature,-60,60);
            EditorUI.DrawLabel(new Rect(0,322,200,28),RimCampaign.Runtime.RcText.T("植被密度 ")+settings.Vegetation.ToString("P0"));
            settings.Vegetation=Widgets.HorizontalSlider(new Rect(220,326,r.width-220,24),settings.Vegetation,0,2);
            EditorUI.DrawLabel(new Rect(0,356,200,28),RimCampaign.Runtime.RcText.T("山体强度 ")+settings.Mountains.ToString("P0"));
            settings.Mountains=Widgets.HorizontalSlider(new Rect(220,360,r.width-220,24),settings.Mountains,0,.7f);
            EditorUI.DrawLabel(new Rect(0,394,r.width,85),RimCampaign.Runtime.RcText.T("清空当前地图的建筑、物品、角色、区域、巡逻和事件布置。\n重新生成地表、原生植被与带天然屋顶的岩石山体；整个操作可撤销。\n相同种子可重现。太空仍为空白真空，需自行建平台和密封建筑。"));
            EditorUI.DrawLabel(new Rect(0,485,r.width,40),error);
            if(EditorUI.Button(new Rect(0,r.height-38,r.width,36),RimCampaign.Runtime.RcText.T("清空布置并初始化（可撤销）"))){
                try {apply(settings);Close();}catch(Exception ex){error=ex.GetBaseException().Message;}
            }
        }
    }
}
