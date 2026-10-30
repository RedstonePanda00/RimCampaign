using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using Verse;
using RimWorld;

namespace RimCampaign.Runtime
{
    public static partial class TerrainInitializer
    {
        public const string NatureLayer="InitializedNature";
        public static bool Generated(Placement p)=>p.Layer==Layer||p.Layer==NatureLayer;
        public static List<Placement> GenerateNature(int width,int height,TerrainInitialization settings) {
            var terrain=new List<Placement>(width*height);var nature=new List<Placement>();
            int seed=TerrainNoise.Seed(settings.Seed);
            var biomes=new[]{Resolve(settings.Biome),settings.SecondaryBiome==null?null:Resolve(settings.SecondaryBiome)}.Where(b=>b!=null).Distinct().ToList();
            var plants=biomes.ToDictionary(b=>b,b=>b.wildPlants.Where(p=>p.plant?.plant!=null&&p.commonality>0&&!p.plant.plant.cavePlant&&!p.plant.plant.RequiresPollution).ToList());
            var rocks=biomes.ToDictionary(b=>b,b=>(b.forceRockTypes!=null&&b.forceRockTypes.Count>0?b.forceRockTypes:
                new List<ThingDef>{DefDatabase<ThingDef>.GetNamed("Granite"),DefDatabase<ThingDef>.GetNamed("Limestone"),DefDatabase<ThingDef>.GetNamed("Slate")}).ToList());
            Placement P(string prefix,Def def,int x,int z,string kind,string layer)=>new Placement{
                Id=prefix+x+"-"+z,Name=def.LabelCap,Kind=kind,Def=Profiles.Ref(def),Position=new Cell{X=x,Z=z},Layer=layer,Faction="Neutral"};
            for(int z=0;z<height;z++)for(int x=0;x<width;x++) {
                var biome=Resolve(TerrainNoise.Secondary(settings,x,z,width)?settings.SecondaryBiome:settings.Biome);
                var ground=TerrainAt(settings,x,z,width);
                double elevation=TerrainNoise.Sample(x/(settings.PatchSize*.7),z/(settings.PatchSize*.7),seed+901)*.8+
                    TerrainNoise.Sample(x/5d,z/5d,seed+902)*.2;
                bool mountain=!biome.inVacuum&&settings.Mountains>0&&elevation>1-settings.Mountains;
                if(mountain) {
                    var list=rocks[biome];int index=Math.Min(list.Count-1,(int)(TerrainNoise.Sample(x/70d,z/70d,seed+903)*list.Count));
                    var rock=list[index];
                    ground=DefDatabase<TerrainDef>.GetNamedSilentFail(rock.defName+"_Rough")??ground;
                    nature.Add(P("rc-rock-",rock,x,z,"Thing",NatureLayer));
                    nature.Add(P("rc-rock-roof-",elevation>1-settings.Mountains*.65f?RoofDefOf.RoofRockThick:RoofDefOf.RoofRockThin,x,z,"Roof",NatureLayer));
                } else if(!biome.inVacuum && TerrainNoise.Sample(x,z,seed+31)<biome.plantDensity*settings.Vegetation*.72f) {
                    var choices=plants[biome].Where(p=>(p.plant.plant.completelyIgnoreFertility||ground.fertility>=p.plant.plant.fertilityMin)&&(p.plant.plant.terrainBlacklist==null||!p.plant.plant.terrainBlacklist.Contains(ground))&&(p.plant.plant.WildTerrainTags.Count==0||(ground.tags!=null&&p.plant.plant.WildTerrainTags.Overlaps(ground.tags)))).ToList();
                    float total=choices.Sum(p=>p.commonality);double pick=TerrainNoise.Sample(x,z,seed+32)*total;
                    foreach(var choice in choices)if((pick-=choice.commonality)<=0){
                        nature.Add(P("rc-plant-",choice.plant,x,z,"Thing",NatureLayer));break;
                    }
                }
                terrain.Add(P("rc-terrain-",ground,x,z,"Terrain",Layer));
            }
            terrain.AddRange(nature);return terrain;
        }
        // Explicit second operation: regenerate natural ground, then overlay the authored layout.
        // Unlike Initialize, this preserves the layout's rules, roles and patrol configuration.
        public static void RegenerateUnderLayout(MapSpec layout) {
            if(layout.TerrainInitialization==null)return;
            Validate(layout.TerrainInitialization);
            var generated=GenerateNature(layout.Width,layout.Height,layout.TerrainInitialization);
            generated.AddRange(layout.Objects.Where(p=>!Generated(p)));
            layout.Objects=generated;layout.Base="Biome";
            ClearLayoutOverlaps(layout);
        }
        public static bool NaturalObject(Placement p) {
            if(p.Kind=="Roof")return p.Def.Name=="RoofRockThin"||p.Def.Name=="RoofRockThick";
            if(p.Kind!="Thing")return false;
            var def=DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name);
            return def?.category==ThingCategory.Plant||def?.building?.isNaturalRock==true||def?.thingCategories?.Any(c=>c.defName.Contains("Chunks"))==true;
        }
        static bool ClearableNature(Placement p)=>NaturalObject(p)&&(p.Kind!="Roof"||Generated(p));
        // Erasers affect only their own layer; a terrain cell always falls back to MapSpec.Terrain.
        public static void EraseSurface(MapSpec spec,Cell cell,bool roof) {
            if(!roof){TerrainSurfaces.Apply(spec,new Placement{Kind="EraseTerrain"},new[]{cell});return;}
            spec.Objects.RemoveAll(p=>p.Kind=="Roof"&&p.Position.X==cell.X&&p.Position.Z==cell.Z);
        }
        public static IEnumerable<Cell> Footprint(Placement p) {
            var def=p.Kind=="Thing"?DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name):null;
            if(def!=null)foreach(var cell in GenAdj.OccupiedRect(new IntVec3(p.Position.X,0,p.Position.Z),new Rot4(p.Rotation),def.size))
                yield return new Cell{X=cell.x,Z=cell.z};
            else yield return p.Position;
        }
        public static void ClearNatureAt(MapSpec spec,IEnumerable<Cell> cells) {
            var set=new HashSet<int>(cells.Where(c=>c.X>=0&&c.Z>=0&&c.X<spec.Width&&c.Z<spec.Height).Select(c=>c.Z*spec.Width+c.X));
            spec.Objects.RemoveAll(p=>ClearableNature(p)&&set.Contains(p.Position.Z*spec.Width+p.Position.X));
            RestoreClearedGround(spec,set);
        }
        public static void ClearLayoutOverlaps(MapSpec spec) {
            var cells=new HashSet<int>();
            foreach(var area in spec.NaturalClearings)for(int z=Math.Max(0,area.Min.Z);z<=Math.Min(spec.Height-1,area.Max.Z);z++)
                for(int x=Math.Max(0,area.Min.X);x<=Math.Min(spec.Width-1,area.Max.X);x++)cells.Add(z*spec.Width+x);
            foreach(var p in spec.Objects.Where(p=>!Generated(p)&&!NaturalObject(p))){
                if(p.Kind=="Marker"||p.Kind=="Roof")continue;
                foreach(var cell in Footprint(p))cells.Add(cell.Z*spec.Width+cell.X);
            }
            spec.Objects.RemoveAll(p=>ClearableNature(p)&&cells.Contains(p.Position.Z*spec.Width+p.Position.X));
            RestoreClearedGround(spec,cells);
        }
        static void RestoreClearedGround(MapSpec spec,HashSet<int> cleared) {
            if(spec.TerrainInitialization==null)return;
            foreach(var p in spec.Objects.Where(p=>p.Layer==Layer&&p.Def.Name.EndsWith("_Rough")&&cleared.Contains(p.Position.Z*spec.Width+p.Position.X))){
                var ground=TerrainAt(spec.TerrainInitialization,p.Position.X,p.Position.Z,spec.Width);
                p.Def=Profiles.Ref(ground);p.Name=ground.LabelCap;
            }
        }
    }
}
