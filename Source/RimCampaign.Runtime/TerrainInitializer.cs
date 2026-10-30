using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimWorld;
using Verse;
using HarmonyLib;

namespace RimCampaign.Runtime
{
    public static partial class TerrainInitializer
    {
        public const string Layer="InitializedTerrain";
        public static BiomeDef Resolve(DefRef value)=>DefDatabase<BiomeDef>.GetNamed(value.Name);
        public static void Validate(TerrainInitialization settings) {
            var errors=TerrainNoise.Errors(settings).ToList();if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            var primary=Resolve(settings.Biome);
            var secondary=settings.SecondaryBiome==null?null:Resolve(settings.SecondaryBiome);
            if((primary.inVacuum||secondary!=null)&&!ModsConfig.OdysseyActive)throw new InvalidOperationException(RimCampaign.Runtime.RcText.T("太空和混合生态需要启用 Odyssey。"));
            if(secondary!=null&&(primary.inVacuum||secondary.inVacuum))throw new InvalidOperationException(RimCampaign.Runtime.RcText.T("太空使用独立地图环境，不能与地表生态混合。"));
            foreach(var biome in new[]{primary,secondary}.Where(b=>b!=null))
                if(!biome.inVacuum&&biome.terrainsByFertility.Count==0)throw new InvalidOperationException(biome.LabelCap+RimCampaign.Runtime.RcText.T(" 没有可用的生态地形表。"));
        }
        public static TerrainDef TerrainAt(TerrainInitialization settings,int x,int z,int width) {
            var biome=Resolve(TerrainNoise.Secondary(settings,x,z,width)?settings.SecondaryBiome:settings.Biome);
            if(biome.inVacuum)return DefDatabase<TerrainDef>.GetNamed("Space");
            int seed=TerrainNoise.Seed(settings.Seed);double scale=settings.PatchSize;
            float fertility=(float)(TerrainNoise.Sample(x/scale,z/scale,seed)*1.35);
            return TerrainThreshold.TerrainAtValue(biome.terrainsByFertility,fertility)
                ?? throw new InvalidOperationException(biome.LabelCap+RimCampaign.Runtime.RcText.T(" 的地形表未覆盖肥沃度 ")+fertility);
        }
        // Full reinitialization is intentionally destructive; the editor wraps this in one undo transaction.
        public static void Initialize(MapSpec spec,TerrainInitialization settings) {
            Validate(settings);
            var generated=GenerateNature(spec.Width,spec.Height,settings);
            spec.Objects=generated;spec.TerrainInitialization=Json.Copy(settings);spec.Base="Biome";
            spec.Terrain=Profiles.Ref(TerrainAt(settings,0,0,spec.Width));
            spec.NaturalClearings.Clear();spec.Regions.Clear();spec.Rules.Clear();spec.Groups.Clear();
            spec.Components.Clear();spec.Rewards.Clear();spec.PatrolRoutes.Clear();spec.CombatPatrols.Clear();
            spec.Objectives.Clear();spec.Hud.Clear();spec.Entry.Clear();spec.Defeat.Clear();spec.Stealth=new StealthSpec();
        }
        public static void ApplyEnvironment(Map map,TerrainInitialization settings) {
            if(settings==null)return;
            Validate(settings);
            if(map.pocketTileInfo==null)throw new InvalidOperationException(RimCampaign.Runtime.RcText.T("生态初始化仅适用于战役独立地图。"));
            var primary=Resolve(settings.Biome);
            map.pocketTileInfo.PrimaryBiome=primary;
            map.pocketTileInfo.temperature=primary.constantOutdoorTemperature??settings.Temperature;
            map.GetComponent<CampaignTerrainEnvironment>().Initialized=true;
            var mixed=map.GetComponent<MixedBiomeMapComponent>();
            mixed.secondaryBiome=settings.SecondaryBiome==null?null:Resolve(settings.SecondaryBiome);
            mixed.biomeGrid=mixed.secondaryBiome==null?null:new bool[map.cellIndices.NumGridCells];
            if(mixed.biomeGrid!=null)foreach(var cell in map.AllCells)
                mixed.biomeGrid[map.cellIndices.CellToIndex(cell)]=TerrainNoise.Secondary(settings,cell.x,cell.z,map.Size.x);
            if(primary.inVacuum)map.weatherManager.TransitionTo(DefDatabase<WeatherDef>.GetNamed("Orbit"));
        }
    }
    public sealed class CampaignTerrainEnvironment:MapComponent
    {
        public bool Initialized;
        public CampaignTerrainEnvironment(Map map):base(map){}
        public override void ExposeData(){Scribe_Values.Look(ref Initialized,"rcTerrainInitialized");}
    }
    [HarmonyPatch(typeof(MapTemperature),"OutdoorTemp",MethodType.Getter)]
    static class CampaignTerrainOutdoorTemperature
    {
        static void Postfix(Map ___map,ref float __result){
            if(___map.IsPocketMap&&___map.GetComponent<CampaignTerrainEnvironment>()?.Initialized==true)
                __result=___map.Biome.constantOutdoorTemperature??___map.pocketTileInfo.temperature;
        }
    }
    [HarmonyPatch(typeof(MapTemperature),"SeasonalTemp",MethodType.Getter)]
    static class CampaignTerrainSeasonalTemperature
    {
        static void Postfix(Map ___map,ref float __result){
            if(___map.IsPocketMap&&___map.GetComponent<CampaignTerrainEnvironment>()?.Initialized==true)
                __result=___map.Biome.constantOutdoorTemperature??___map.pocketTileInfo.temperature;
        }
    }
    [HarmonyPatch(typeof(Map),"Biomes",MethodType.Getter)]
    static class CampaignTerrainBiomes
    {
        static void Postfix(Map __instance,ref IEnumerable<BiomeDef> __result){
            var secondary=__instance.GetComponent<MixedBiomeMapComponent>()?.secondaryBiome;
            if(__instance.GetComponent<CampaignTerrainEnvironment>()?.Initialized==true&&secondary!=null)
                __result=new[]{__instance.Biome,secondary}.Distinct();
        }
    }
}
