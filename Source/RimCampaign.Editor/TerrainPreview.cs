using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using Verse;
using RimCampaign.Runtime;
namespace RimCampaign.Editor
{
    public partial class Workspace
    {
        readonly List<KeyValuePair<CellRect,TerrainDef>> floorPreviewRuns=new List<KeyValuePair<CellRect,TerrainDef>>();
        int terrainPreviewRevision;
        // RW 1.7 TerrainHard samples world XZ / 16. IMGUI flips the vertical
        // texture coordinate for us. Split at repeats: UI samplers clamp UVs.
        const int FloorTexturePeriod=16;
        static Rect FloorUV(CellRect cells)=>new Rect((cells.minX%FloorTexturePeriod)/(float)FloorTexturePeriod,
            (cells.minZ%FloorTexturePeriod)/(float)FloorTexturePeriod,cells.Width/(float)FloorTexturePeriod,1f/FloorTexturePeriod);
        void AddFloorPreviewRun(int start,int end,int z,TerrainDef def){
            while(start<=end){
                int count=System.Math.Min(end-start+1,FloorTexturePeriod-start%FloorTexturePeriod);
                floorPreviewRuns.Add(new KeyValuePair<CellRect,TerrainDef>(new CellRect(start,z,count,1),def));start+=count;
            }
        }
        void DrawTerrainPreview(Rect rect,Vector2 viewport) {
            if(terrainPreview==null||terrainPreviewMap!=Map||terrainPreviewDirty){
                // Reuse the GPU resource when dimensions are unchanged. Edits can
                // arrive after the current GUI pass has already referenced it.
                if(terrainPreview==null||terrainPreview.width!=Map.Width||terrainPreview.height!=Map.Height){
                    if(terrainPreview!=null)Object.Destroy(terrainPreview);
                    terrainPreview=new Texture2D(Map.Width,Map.Height){filterMode=FilterMode.Point};
                }
                var pixels=new Color[Map.Width*Map.Height];
                var ground=TerrainColor(Map.Terrain.Name);
                for(int i=0;i<pixels.Length;i++)pixels[i]=ground;
                foreach(var p in Map.Objects.OrderBy(TerrainSurfaces.SpawnOrder))if(p.Kind=="Terrain"&&(editMode!="Terrain"||!TerrainSurfaces.IsFloor(p))&&p.Position.X>=0&&p.Position.X<Map.Width&&p.Position.Z>=0&&p.Position.Z<Map.Height)
                    pixels[p.Position.Z*Map.Width+p.Position.X]=TerrainColor(p.Def.Name);
                terrainPreview.SetPixels(pixels);terrainPreview.Apply();
                floorPreviewRuns.Clear();
                if(editMode!="Terrain"){
                    var floors=Map.Objects.Where(p=>p.Kind=="Terrain"&&TerrainSurfaces.IsFloor(p))
                        .GroupBy(p=>p.Position.Z*Map.Width+p.Position.X).Select(g=>g.OrderBy(TerrainSurfaces.SpawnOrder).Last());
                    foreach(var row in floors.GroupBy(p=>new{p.Position.Z,p.Def.Name})){
                        var def=DefDatabase<TerrainDef>.GetNamedSilentFail(row.Key.Name);
                        int start=-1,last=-1;
                        foreach(var p in row.OrderBy(p=>p.Position.X)){
                            if(start<0)start=p.Position.X;
                            else if(p.Position.X!=last+1){AddFloorPreviewRun(start,last,row.Key.Z,def);start=p.Position.X;}
                            last=p.Position.X;
                        }
                        if(start>=0)AddFloorPreviewRun(start,last,row.Key.Z,def);
                    }
                }
                terrainPreviewMap=Map;terrainPreviewDirty=false;terrainPreviewRevision++;
            }
            GUI.DrawTexture(rect,terrainPreview);
            var old=GUI.color;
            foreach(var run in floorPreviewRuns){
                var cells=run.Key;var def=run.Value;
                var at=new Rect(rect.x+cells.minX*zoom,rect.y+(Map.Height-1-cells.minZ)*zoom,cells.Width*zoom,zoom);
                if(at.xMax<0||at.yMax<0||at.x>viewport.x||at.y>viewport.y)continue;
                var texture=def?.graphic?.MatSingle?.mainTexture;
                if(texture==null)continue;
                GUI.color=def.DrawColor;
                GUI.DrawTextureWithTexCoords(at,texture,FloorUV(cells));
            }
            GUI.color=old;
            foreach(var p in HighlightedPlacements())if(p.Kind=="Terrain")
                SelectionBorder(new Rect(rect.x+p.Position.X*zoom,rect.y+(Map.Height-1-p.Position.Z)*zoom,zoom,zoom));
        }
    }
}
