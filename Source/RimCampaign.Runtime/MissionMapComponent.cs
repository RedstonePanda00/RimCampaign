using System;
using System.Linq;
using RimCampaign.Core;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime {
    // Each visited mission map owns its runner; the open campaign stays in its own
    // GameComponent. Existing action modules receive the same controller interface.
    public sealed class MissionMapComponent : MapComponent {
        public CampaignController Controller;
        public MissionMapComponent(Map map):base(map){}
        public void Attach(MissionMapSpec mission,IntVec3 center,Rot4 rotation) {
            if(Controller!=null)throw new InvalidOperationException("This map already contains a mission runner.");
            if(rotation!=Rot4.North)throw new InvalidOperationException("Mission maps preserve their authored orientation.");
            var project=Json.Copy(mission.Project);var spec=project.Maps.Single();
            int dx=center.x-(spec.Width-1)/2,dz=center.z-(spec.Height-1)/2;
            if(dx<0||dz<0||dx+spec.Width>map.Size.x||dz+spec.Height>map.Size.z)throw new InvalidOperationException("Mission structure exceeds destination map.");
            void Move(Cell p){if(p!=null){p.X+=dx;p.Z+=dz;}}
            foreach(var p in spec.Objects)Move(p.Position);
            foreach(var r in spec.Rules)Move(r.Position);
            foreach(var r in spec.Regions.Concat(spec.NaturalClearings)){Move(r.Min);Move(r.Max);}
            foreach(var r in spec.PatrolRoutes.Concat(spec.Stealth.Routes))foreach(var p in r.Points)Move(p.Position);
            foreach(var d in spec.Stealth.Devices)Move(d.Position);
            spec.Width=map.Size.x;spec.Height=map.Size.z;
            // Native biome vegetation outside authored objects is retained. The mission
            // rectangle itself must not be sealed by mountain roofs or generated ruins.
            foreach(var cell in new CellRect(dx,dz,mission.Project.Maps[0].Width,mission.Project.Maps[0].Height)) {
                map.roofGrid.SetRoof(cell,null);
                foreach(var thing in cell.GetThingList(map).ToList())if(thing is Building||thing is Filth||thing.def.thingCategories?.Any(c=>c.defName.Contains("Chunks"))==true){
                    if(thing.def.destroyable)thing.Destroy(DestroyMode.Vanish);else thing.DeSpawn();
                }
            }
            // Scope faction identities to this instance, while keeping object/trigger IDs stable.
            var old=spec.Id;spec.Id="structure-mission-"+map.uniqueID;
            foreach(var n in project.Campaigns.SelectMany(c=>c.Nodes))if(n.Map==old)n.Map=spec.Id;
            Controller=new CampaignController(Current.Game){Map=map,MapMission=true};
            Controller.InitializeMapMission(project);
            Controller.StartMapMission();
        }
        public override void ExposeData(){Scribe_Deep.Look(ref Controller,"structureMission",Current.Game);}
        public override void FinalizeInit(){base.FinalizeInit();if(Controller!=null){Controller.Map=map;Controller.MapMission=true;Controller.LoadedGame();}}
        public override void MapComponentTick(){Controller?.GameComponentTick();}
        public override void MapComponentUpdate(){Controller?.GameComponentUpdate();}
        public override void MapComponentOnGUI(){if(Find.CurrentMap==map)Controller?.GameComponentOnGUI();}
    }
    public partial class CampaignController {
        public bool MapMission;
        public static CampaignController ForMap(Map map) {
            var local=map?.GetComponent<MissionMapComponent>()?.Controller;if(local!=null)return local;
            var campaign=Current.Game?.GetComponent<CampaignController>();return campaign?.Map==map?campaign:null;
        }
        public static CampaignController Focused=>ForMap(Find.CurrentMap)??Current.Game?.GetComponent<CampaignController>();
        internal void InitializeMapMission(Project project) {
            Project=project;CampaignId=project.Campaigns[0].Id;NodeId=Campaign.Start;BindActors();
            foreach(var placement in MapSpec.Objects.Where(p=>p.SpawnAtStart&&(p.Kind=="Thing"||p.Kind=="Terrain")))foreach(var cell in StructureSpawner.Bounds(placement))
                foreach(var t in cell.GetThingList(Map).ToList())if(t is Plant)t.Destroy(DestroyMode.Vanish);
            foreach(var placement in MapSpec.Objects.Where(p=>p.Kind!="Marker"&&p.SpawnAtStart).OrderBy(p=>p.Kind=="Roof"?5:TerrainSurfaces.SpawnOrder(p)))Place(placement,Map);
            GenerateMapRewards();CampaignMapModules.Apply(this);
        }
        internal void StartMapMission() {
            engine=new Engine(MapSpec,State,this);
            if(!State.Variables.ContainsKey("rc:mission-started")){engine.Start();State.Variables["rc:mission-started"]=1;}
        }
        internal bool FinishMapMission() {
            if(!MapMission)return false;
            if(State.Variables.ContainsKey("rc:mission-result-reported"))return true;
            State.Variables["rc:mission-result-reported"]=1;
            // Keep completion local: no campaign transitions, checkpoint reload or pause.
            if(Map.Parent is MissionSite site)site.ReportResult(State.Phase=="Succeeded");
            Messages.Message(RcText.T(MapSpec.Name)+": "+RcText.T(State.Phase=="Succeeded"?"任务完成":"任务失败"),State.Phase=="Succeeded"?MessageTypeDefOf.PositiveEvent:MessageTypeDefOf.NegativeEvent,false);
            return true;
        }
    }
}
