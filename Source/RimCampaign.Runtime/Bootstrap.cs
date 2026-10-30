using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimCampaign.Runtime
{
    public class CampaignDef : Def
    {
        public string projectFile; public string campaignId;
        public Project LoadProject() {
            var path=modContentPack.foldersToLoadDescendingOrder.Select(f=>Path.Combine(f,projectFile)).FirstOrDefault(File.Exists);
            if(path==null)throw new FileNotFoundException(projectFile);
            var p=Json.Load<Project>(path);
            var report=Dependencies.Scan(p,Catalog());
            var issues=Validation.Check(p).Select(x=>x.ToString()).Concat(report.Errors).ToList();
            var active=LoadedModManager.RunningMods.Select(m=>m.PackageIdPlayerFacing.ToLowerInvariant()).ToList();
            int index=active.IndexOf(modContentPack.PackageIdPlayerFacing.ToLowerInvariant());
            foreach(var d in report.LoadAfter)if(active.IndexOf(d)>index)issues.Add("Load campaign after "+d);
            if(issues.Count>0)throw new InvalidOperationException(string.Join("\n",issues));return p;
        }
        public static IEnumerable<Dependency> Catalog() => LoadedModManager.RunningMods.Select(m=>new Dependency {
            PackageId=m.PackageIdPlayerFacing.ToLowerInvariant(),Name=m.Name,
            Requires=m.ModMetaData.Dependencies.Select(d=>d.packageId.ToLowerInvariant()).ToList(),
            After=m.ModMetaData.LoadAfter.Concat(m.ModMetaData.ForceLoadAfter).Select(x=>x.ToLowerInvariant()).ToList(),
            Before=m.ModMetaData.LoadBefore.Concat(m.ModMetaData.ForceLoadBefore).Select(x=>x.ToLowerInvariant()).ToList()
        });
    }
    public class CampaignMod : Mod
    {
        public CampaignMod(ModContentPack content):base(content) { new Harmony("rimcampaign.framework").PatchAll(); }
    }
    [StaticConstructorOnStartup]
    public static class ModuleLoader
    {
        static ModuleLoader() {
            foreach(var assembly in LoadedModManager.RunningMods.SelectMany(m=>m.assemblies.loadedAssemblies)) {
                try { Modules.Discover(assembly); } catch(Exception ex) { Log.Error("[RimCampaign] Module discovery: "+ex); }
            }
        }
    }
    public class ScenPart_Campaign : ScenPart_ForcedMap
    {
        public CampaignDef campaign;
        public override void ExposeData() { base.ExposeData();Scribe_Defs.Look(ref campaign,"campaign"); }
        public override void PostWorldGenerate() {
            Find.GameInitData.startingTile=TileFinder.RandomStartingTile();
            Find.GameInitData.mapGeneratorDef=DefDatabase<MapGeneratorDef>.GetNamed("RC_Pocket");
        }
        public override IEnumerable<Page> GetConfigPages() { yield return new Page_CampaignStart(campaign); }
    }
    public class Page_CampaignStart : Page
    {
        readonly CampaignDef def;
        public Page_CampaignStart(CampaignDef def) { this.def=def; }
        public override string PageTitle=>"RimCampaign";
        public override void DoWindowContents(Rect rect) { Widgets.DrawBoxSolid(rect,Color.black); }
        public override void PostOpen() {
            base.PostOpen();
            try {
                var controller=Current.Game.GetComponent<CampaignController>();controller.Prepare(def);
                var pawns=controller.Actors.Values.Distinct().ToList();
                Find.GameInitData.startingAndOptionalPawns=pawns;Find.GameInitData.startingPawnCount=pawns.Count;
                LongEventHandler.ExecuteWhenFinished(DoNext);
            }catch(Exception ex){Find.WindowStack.Add(new Dialog_MessageBox(ex.Message));}
        }
    }
    [HarmonyPatch(typeof(MapGenerator),nameof(MapGenerator.GenerateMap))]
    static class StartingPocket
    {
        static bool Prefix(ref Map __result,bool isPocketMap) {
            var c=Current.Game?.GetComponent<CampaignController>();
            if(isPocketMap || CampaignMaps.GeneratingNative || c?.Starting!=true || !c.StoryActive || Current.Game.Maps.Count!=0)return true;
            var m=c.MapSpec;
            __result=CampaignMaps.Generate(m,Find.GameInitData.startingTile);
            c.Map=__result;return false;
        }
    }
    public static class CampaignMaps
    {
        internal static bool GeneratingNative;
        public static Map Generate(MapSpec spec,PlanetTile anchor){
            if(spec.Base=="PocketMission"){
                var native=(MapParent)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("RC_MissionPreview"));
                native.Tile=anchor;Find.WorldObjects.Add(native);
                GeneratingNative=true;
                try{return MapGenerator.GenerateMap(new IntVec3(spec.Width,1,spec.Height),native,DefDatabase<MapGeneratorDef>.GetNamed("RC_MissionTerrain"));}
                finally{GeneratingNative=false;}
            }
            var parent=(PocketMapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.PocketMap);
            bool space=spec.TerrainInitialization!=null&&TerrainInitializer.Resolve(spec.TerrainInitialization.Biome).inVacuum;
            parent.Tile=anchor;parent.mapGenerator=DefDatabase<MapGeneratorDef>.GetNamed(space?"RC_PocketSpace":"RC_Pocket");
            var map=MapGenerator.GenerateMap(new IntVec3(spec.Width,1,spec.Height),parent,parent.mapGenerator,isPocketMap:true);
            Find.World.pocketMaps.Add(parent);return map;
        }
    }
    public class GenStep_Campaign : GenStep
    {
        public override int SeedPart=>11930731;
        public override void Generate(Map map,GenStepParams parms) {
            var c=Current.Game.GetComponent<CampaignController>();
            var spec=c.MapSpec;
            TerrainInitializer.ApplyEnvironment(map,spec.TerrainInitialization);
            TerrainInitializer.ClearLayoutOverlaps(spec);
            foreach(var cell in map.AllCells) {
                map.terrainGrid.SetTerrain(cell,DefDatabase<TerrainDef>.GetNamed(spec.Terrain.Name));
                map.fogGrid.Unfog(cell);
            }
            MapGenerator.PlayerStartSpot=map.Center;
            foreach(var o in spec.Objects.Where(o=>o.Kind!="Pawn"&&o.Kind!="Marker"&&o.SpawnAtStart).OrderBy(TerrainSurfaces.SpawnOrder)) c.Place(o,map);
        }
    }
}
