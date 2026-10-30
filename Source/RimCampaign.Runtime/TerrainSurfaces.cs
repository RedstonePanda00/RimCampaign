using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using Verse;

namespace RimCampaign.Runtime
{
    public static class TerrainSurfaces
    {
        public static bool IsFloor(TerrainDef def)=>def!=null&&(def.IsFloor||def.layerable||def.isFoundation);
        public static bool IsFloor(Placement p)=>p.Kind=="Terrain"&&IsFloor(DefDatabase<TerrainDef>.GetNamedSilentFail(p.Def.Name));
        public static int SpawnOrder(Placement p){
            if(p.Kind!="Terrain")return 3;
            var def=DefDatabase<TerrainDef>.GetNamedSilentFail(p.Def.Name);
            return def?.isFoundation==true?1:IsFloor(def)?2:0;
        }
        public static void Place(Map map,IntVec3 cell,TerrainDef def){
            var grid=map.terrainGrid;
            if(def.isFoundation){grid.SetTerrain(cell,def);return;}
            var top=grid.TerrainAt(cell);
            if(IsFloor(def)){
                var under=grid.UnderTerrainAt(cell)??top;
                grid.SetTerrain(cell,def);
                if(under!=null)grid.SetUnderTerrain(cell,under);
            }else if(IsFloor(top)&&!top.isFoundation)grid.SetUnderTerrain(cell,def);
            else grid.SetTerrain(cell,def);
        }
        public static void Apply(MapSpec map,Placement brush,IEnumerable<Cell> cells){
            bool floor=brush.Kind=="EraseFloor"||brush.Kind=="Terrain"&&IsFloor(brush);
            var selected=cells.Where(c=>c.X>=0&&c.Z>=0&&c.X<map.Width&&c.Z<map.Height).ToList();
            var keys=new HashSet<int>(selected.Select(c=>c.Z*map.Width+c.X));
            map.Objects.RemoveAll(p=>p.Kind=="Terrain"&&IsFloor(p)==floor&&keys.Contains(p.Position.Z*map.Width+p.Position.X));
            if(brush.Kind!="Terrain")return;
            TerrainInitializer.ClearNatureAt(map,selected);
            foreach(var cell in selected)map.Objects.Add(new Placement{Kind="Terrain",Name=brush.Name,Def=Json.Copy(brush.Def),Position=cell,Layer=floor?"Floor":"Terrain",Faction="Neutral"});
        }
    }
}
