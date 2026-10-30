using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimCampaign.Core;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace RimCampaign.Runtime {
    public sealed class MissionMapDef : Def {
        public string missionFile;
        public SitePartDef sitePart;
        MissionMapSpec cached;
        public MissionMapSpec Template {
            get {
                if(cached!=null)return cached;
                var path=modContentPack.foldersToLoadDescendingOrder.Select(f=>Path.Combine(f,missionFile)).FirstOrDefault(File.Exists);
                if(path==null)throw new FileNotFoundException(missionFile);
                var value=Json.Load<MissionMapSpec>(path);MissionMaps.Validate(value);return cached=value;
            }
        }
        public MissionSite CreateSite(PlanetTile tile,Faction faction=null){
            MissionMaps.Validate(Template);
            var part=new SitePartDefWithParams(sitePart,sitePart.Worker.GenerateDefaultParams(0,tile,faction));
            var site=(MissionSite)SiteMaker.MakeSite(new[]{part},tile,faction,true,DefDatabase<WorldObjectDef>.GetNamed("RC_MissionSite"));
            site.Mission=this;site.customLabel=LabelCap;return site;
        }
    }
    public sealed class MissionSite : Site {
        public MissionMapDef Mission;
        public string Result="";
        public override MapGeneratorDef MapGeneratorDef=>DefDatabase<MapGeneratorDef>.GetNamed("RC_MissionTerrain");
        public override void ExposeData(){base.ExposeData();Scribe_Defs.Look(ref Mission,"missionMap");Scribe_Values.Look(ref Result,"missionResult","");}
        public void ReportResult(bool success){
            if(Result!="")return;Result=success?"MissionSucceeded":"MissionFailed";
            QuestUtility.SendQuestTargetSignals(questTags,Result,this.Named("SUBJECT"));
        }
    }
    public sealed class GenStep_MissionMap : GenStep {
        public MissionMapDef mission;
        public override int SeedPart=>74862139;
        public override void Generate(Map map,GenStepParams parms){
            var template=mission??(map.Parent as MissionSite)?.Mission;
            if(template!=null)map.GetComponent<MissionMapComponent>().Attach(template.Template,map.Center,Rot4.North);
            else if(map.Parent.def.defName=="RC_MissionPreview"){
                var c=Current.Game.GetComponent<CampaignController>();c.Map=map;
                foreach(var cell in map.AllCells){map.roofGrid.SetRoof(cell,null);foreach(var t in cell.GetThingList(map).ToList())if(t is Building||t is Filth){if(t.def.destroyable)t.Destroy(DestroyMode.Vanish);else t.DeSpawn();}}
                foreach(var p in c.MapSpec.Objects.Where(p=>p.Kind!="Pawn"&&p.Kind!="Marker"&&p.SpawnAtStart).OrderBy(p=>p.Kind=="Roof"?5:TerrainSurfaces.SpawnOrder(p)))c.Place(p,map);
            }
        }
    }
    // Fits native QuestNode_Sequence / GetSiteTile / SpawnWorldObject / End.
    // The caller chooses reward, faction, tile, activation and completion handling.
    public sealed class QuestNode_GenerateMissionSite : QuestNode {
        public SlateRef<MissionMapDef> mission;
        public SlateRef<PlanetTile> tile;
        public SlateRef<Faction> faction;
        public SlateRef<string> storeAs;
        protected override bool TestRunInt(Slate slate)=>mission.GetValue(slate)!=null;
        protected override void RunInt(){
            var slate=QuestGen.slate;var site=mission.GetValue(slate).CreateSite(tile.GetValue(slate),faction.GetValue(slate));
            slate.Set(storeAs.GetValue(slate)??"missionSite",site);
            QuestUtility.AddQuestTag(ref site.questTags,QuestGenUtility.HardcodedTargetQuestTagWithQuestID(storeAs.GetValue(slate)??"missionSite"));
        }
    }
}
