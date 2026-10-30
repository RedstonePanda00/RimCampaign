using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimCampaign.Runtime {
    public sealed class CampaignStructureDef : Def {
        public string structureFile;
        StructureSpec cached;
        public StructureSpec Layout {
            get {
                if(cached!=null)return cached;
                var path=modContentPack.foldersToLoadDescendingOrder.Select(f=>Path.Combine(f,structureFile)).FirstOrDefault(File.Exists);
                if(path==null)throw new FileNotFoundException(structureFile);
                var value=Json.Load<StructureSpec>(path);Structures.Validate(value);return cached=value;
            }
        }
    }
    public static class StructureSpawner {
        public static StructureSpec Resolve(string id,Project project=null) =>
            project?.Structures?.FirstOrDefault(s=>s.Id==id||Structures.DefName(s)==id)??DefDatabase<CampaignStructureDef>.GetNamed(id).Layout;
        public static CellRect Bounds(Placement p) => GenAdj.OccupiedRect(new IntVec3(p.Position.X,0,p.Position.Z),new Rot4(p.Rotation),
            p.Kind=="Thing"?DefDatabase<ThingDef>.GetNamed(p.Def.Name).size:IntVec2.One);
        // Center is the center cell of the selected footprint, including even-sized buildings.
        public static List<Placement> At(StructureSpec s,IntVec3 center,Rot4 rotation) {
            Structures.Validate(s);
            var root=PrefabUtility.GetRoot(center,new IntVec2(s.Width,s.Height),rotation);
            var result=Json.Copy(s.Objects);
            foreach(var p in result) {
                var before=Bounds(p);
                var twiceCenter=new IntVec3(before.minX+before.maxX,0,before.minZ+before.maxZ).RotatedBy(rotation);
                p.Rotation=(p.Rotation+rotation.AsInt)%4;p.Position=new Cell();
                var zero=Bounds(p);
                p.Position=new Cell{X=root.x+(int)Math.Round((twiceCenter.x-zero.minX-zero.maxX)*.5,MidpointRounding.AwayFromZero),Z=root.z+(int)Math.Round((twiceCenter.z-zero.minZ-zero.maxZ)*.5,MidpointRounding.AwayFromZero)};
            }
            return result;
        }
        public static List<Thing> Spawn(StructureSpec s,Map map,IntVec3 center,Rot4 rotation,string arrival="Ground",Func<string,Faction> factionFor=null,bool clear=false) {
            if(arrival!="Ground"&&arrival!="Pods")throw new InvalidOperationException("Unknown structure arrival: "+arrival);

            var placements=At(s,center,rotation);
            if(arrival=="Pods"&&placements.Any(p=>p.Kind=="Terrain"||p.Kind=="Roof"))throw new InvalidOperationException("Drop-pod structures must contain only things and PawnKinds; terrain and roofs require Ground arrival.");
            var scope=new MapSpec{Id="structure-"+s.Id,Factions=s.Factions};
            factionFor??=id=>Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(scope,id);
            var factions=placements.Select(p=>p.Faction).Distinct().ToDictionary(id=>id,id=>factionFor(id));
            var footprint=GenAdj.OccupiedRect(center,rotation,new IntVec2(s.Width,s.Height));
            if(!footprint.FullyContainedWithin(new CellRect(0,0,map.Size.x,map.Size.z)))throw new InvalidOperationException("Structure extends outside map: "+s.Name);
            // Validate the entire operation before changing terrain or spawning any object.
            foreach(var p in placements) {
                var bounds=Bounds(p);
                if(!bounds.FullyContainedWithin(footprint))throw new InvalidOperationException("Invalid structure footprint: "+p.Name);
                if(p.Kind=="Thing")PlacementStorage.Validate(p);
                else if(p.Kind=="PawnKind")DefDatabase<PawnKindDef>.GetNamed(p.Def.Name);
                else if(p.Kind=="Terrain")DefDatabase<TerrainDef>.GetNamed(p.Def.Name);
                else if(!string.IsNullOrEmpty(p.Def.Name))DefDatabase<RoofDef>.GetNamed(p.Def.Name);
                if(!clear&&p.Kind=="Thing"&&DefDatabase<ThingDef>.GetNamed(p.Def.Name).category==ThingCategory.Building&&bounds.Any(cell=>cell.GetEdifice(map)!=null))
                    throw new InvalidOperationException("Structure overlaps an existing building: "+p.Name);
                if(arrival=="Pods"&&bounds.Any(c=>c.GetRoof(map)?.isThickRoof==true))throw new InvalidOperationException("Structure drop pods cannot penetrate thick roofs.");
            }
            if(clear)foreach(var cell in footprint) {
                map.roofGrid.SetRoof(cell,null);
                foreach(var t in cell.GetThingList(map).ToList())if(t is Building||t is Plant||t is Filth) {
                    if(t.def.destroyable)t.Destroy(DestroyMode.Vanish);else t.DeSpawn();
                }
            }
            foreach(var p in placements.Where(p=>p.Kind=="Terrain"))TerrainSurfaces.Place(map,new IntVec3(p.Position.X,0,p.Position.Z),DefDatabase<TerrainDef>.GetNamed(p.Def.Name));
            var result=new List<Thing>();var instances=new Dictionary<string,Thing>();
            foreach(var p in placements.Where(p=>p.Kind=="Thing"||p.Kind=="PawnKind")) {
                var faction=factions[p.Faction];
                Thing thing;
                if(p.Kind=="PawnKind")thing=Profiles.Generate(p.Def,faction);
                else {
                    var def=DefDatabase<ThingDef>.GetNamed(p.Def.Name);
                    thing=CampaignItems.Make(def,PlacementStorage.StuffFor(def,p.Stuff),p.Book);
                    thing.stackCount=Math.Min(Math.Max(1,p.Count),def.stackLimit);PlacementGraphics.Apply(p,thing);
                    if(def.CanHaveFaction)thing.SetFaction(faction);
                }
                var cell=new IntVec3(p.Position.X,0,p.Position.Z);var rot=new Rot4(p.Rotation);
                if(arrival=="Pods") {
                    var info=new ActiveTransporterInfo{openDelay=60,leaveSlag=false,despawnPodBeforeSpawningThing=true,spawnWipeMode=WipeMode.Vanish,setRotation=rot,moveItemsAsideBeforeSpawning=true};
                    info.innerContainer.TryAdd(thing);
                    DropPodUtility.MakeDropPodAt(cell,map,info,faction);
                    if(p.Contents?.Count>0)map.GetComponent<StructureLandingContents>().Add(thing,p);
                } else {GenSpawn.Spawn(thing,cell,map,rot);PlacementStorage.Populate(thing,p);}
                result.Add(thing);
                instances[p.Id]=thing;
            }
            foreach(var p in placements.Where(p=>p.Kind=="Roof"))map.roofGrid.SetRoof(new IntVec3(p.Position.X,0,p.Position.Z),string.IsNullOrEmpty(p.Def.Name)?null:DefDatabase<RoofDef>.GetNamed(p.Def.Name));
            var patrolling=new HashSet<Pawn>();
            if(s.Patrols?.Count>0){
                var component=map.GetComponent<CombatPatrolMapComponent>();
                if(component==null){component=new CombatPatrolMapComponent(map);map.components.Add(component);}
                var root=PrefabUtility.GetRoot(center,new IntVec2(s.Width,s.Height),rotation);var routeIds=new Dictionary<string,string>();
                foreach(var original in s.PatrolRoutes){var route=Json.Copy(original);routeIds[route.Id]=route.Id=Guid.NewGuid().ToString("N");
                    foreach(var point in route.Points){var at=new IntVec3(point.Position.X,0,point.Position.Z).RotatedBy(rotation)+root;point.Position=new Cell{X=at.x,Z=at.z};point.Facing=(point.Facing+rotation.AsInt)%4;point.Marker="";}
                    component.Routes.Add(route);
                }
                foreach(var binding in s.Patrols){var pawn=(Pawn)instances[binding.Pawn];component.Units.Add(new CombatPatrolMemory{PawnId=pawn.ThingID,Route=routeIds[binding.Route],Radius=binding.Radius});patrolling.Add(pawn);}
                component.Enabled=true;
            }
            foreach(var group in result.OfType<Pawn>().Where(p=>p.Faction!=null&&p.Faction!=Faction.OfPlayer&&!patrolling.Contains(p)).GroupBy(p=>p.Faction))
                LordMaker.MakeNewLord(group.Key,new LordJob_DefendBase(group.Key,center,120000),map,group.ToList());
            return result;
        }
    }
    // Storage shelves need a map before their contents can be placed. Native pods own
    // and save the actual objects; this component only completes their authored contents.
    public sealed class StructureLandingContents : MapComponent {
        public sealed class Pending : IExposable {
            public Thing Thing;public string Placement;
            public void ExposeData(){Scribe_References.Look(ref Thing,"thing");Scribe_Values.Look(ref Placement,"placement");}
        }
        List<Pending> pending=new List<Pending>();
        public StructureLandingContents(Map map):base(map){}
        public void Add(Thing thing,Placement p)=>pending.Add(new Pending{Thing=thing,Placement=Json.Write(p)});
        public override void ExposeData(){Scribe_Collections.Look(ref pending,"structureLandingContents",LookMode.Deep);}
        public override void MapComponentTick(){
            for(int i=pending.Count-1;i>=0;i--){var p=pending[i];if(p.Thing==null||p.Thing.Destroyed){pending.RemoveAt(i);continue;}if(!p.Thing.Spawned)continue;
                pending.RemoveAt(i);PlacementStorage.Populate(p.Thing,Json.Read<Placement>(p.Placement));}
        }
    }
    [CampaignModule("rc.spawnStructure","Action","Target: structure ID/Def. Origin: center marker. Arrival: Ground or Pods. Text: optional map faction override; empty preserves authored factions.")]
    public sealed class SpawnStructure : IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta) {
            var c=(CampaignController)host;
            var marker=c.MapSpec.Objects.Single(p=>p.Id==a.Origin&&p.Kind=="Marker");
            var s=StructureSpawner.Resolve(a.Target,c.Project);
            var scope=new MapSpec{Id="structure-"+s.Id,Factions=s.Factions};var registry=Current.Game.GetComponent<CampaignFactionRegistry>();
            StructureSpawner.Spawn(s,c.Map,new IntVec3(marker.Position.X,0,marker.Position.Z),new Rot4(marker.Rotation),a.Arrival,
                id=>registry.Resolve(string.IsNullOrEmpty(a.Text)?scope:c.MapSpec,string.IsNullOrEmpty(a.Text)?id:a.Text));
            return true;
        }
    }
    // Native map generators can reference the same exported definition directly.
    public sealed class GenStep_CampaignStructure : GenStep {
        public CampaignStructureDef structure;
        public string arrival="Ground";
        public bool clear=true;
        public override int SeedPart=>74862137;
        public override void Generate(Map map,GenStepParams parms)=>StructureSpawner.Spawn(structure.Layout,map,map.Center,Rot4.North,arrival,
            id=>id=="Neutral"?null:id=="Player"?Faction.OfPlayer:map.ParentFaction,clear);
    }
}
