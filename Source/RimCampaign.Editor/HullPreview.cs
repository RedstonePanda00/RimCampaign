using System.Collections.Generic;
using System.Linq;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor {
 public partial class Workspace {
  readonly List<HullCorner> hullPreview=new();
  static readonly Dictionary<(HullLinkExtension,int,int,bool,int),Texture2D> hullPreviewTextures=new();
  void RebuildHullPreview(){
   hullPreview.Clear();var edifices=new Dictionary<IntVec3,ThingDef>();var foundations=new HashSet<IntVec3>();var candidates=new HashSet<IntVec3>();
   foreach(var p in Map.Objects){
    if(p.Kind=="Terrain"){if(DefDatabase<TerrainDef>.GetNamedSilentFail(p.Def.Name)?.IsSubstructure==true)foundations.Add(new IntVec3(p.Position.X,0,p.Position.Z));continue;}
    if(p.Kind!="Thing")continue;var def=DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name);if(def?.building?.isEdifice!=true)continue;
    foreach(var cell in TerrainInitializer.Footprint(p)){
     var at=new IntVec3(cell.X,0,cell.Z);edifices[at]=def;
     if(def.GetModExtension<HullLinkExtension>()!=null)foreach(var delta in GenAdj.CardinalDirections)candidates.Add(at+delta);
    }
   }
   foreach(var cell in candidates)if(cell.x>=0&&cell.z>=0&&cell.x<Map.Width&&cell.z<Map.Height&&HullConnections.TryCorner(cell,p=>edifices.TryGetValue(p,out var def)?def:null,p=>foundations.Contains(p),out var corner))hullPreview.Add(corner);
  }
  // Cache a supersampled GUI projection of the exact runtime polygons.
  // This is a preview renderer, not a second set of authored wall assets.
  static Texture2D HullPreviewTexture(HullCorner corner,int faceIndex){
   var key=(corner.Style,corner.X,corner.Z,corner.Full,faceIndex);if(hullPreviewTextures.TryGetValue(key,out var texture))return texture;
   const int size=128;var origin=new Vector2(corner.X>0?0:-1,corner.Z>0?0:-1);
   var local=new HullCorner(IntVec3.Zero,corner.X,corner.Z,corner.Full,corner.Style);var faces=HullConnections.Faces(local).Skip(faceIndex).Take(1).ToList();var pixels=new Color[size*size];
   bool Inside(Vector2 p,Vector2[] poly){bool sign=false;for(int i=0;i<poly.Length;i++){var a=poly[i];var b=poly[(i+1)%poly.Length];float cross=(b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);if(i==0)sign=cross>=0;else if((cross>=0)!=sign)return false;}return true;}
   for(int y=0;y<size;y++)for(int x=0;x<size;x++){
    Color sum=Color.clear;int covered=0;
    for(int sy=0;sy<2;sy++)for(int sx=0;sx<2;sx++){
     var p=origin+new Vector2((x+(sx+.5f)/2)*2/size,(y+(sy+.5f)/2)*2/size);Color sample=Color.clear;
     foreach(var face in faces)if(Inside(p,face.Points))sample=face.Color;
     if(sample.a>0){sum+=sample;covered++;}
    }
    if(covered>0){sum/=covered;sum.a=covered/4f;}pixels[y*size+x]=sum;
   }
   texture=new Texture2D(size,size,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};texture.SetPixels(pixels);texture.Apply();hullPreviewTextures[key]=texture;return texture;
  }
  void DrawHullPreview(float width,float height){
   var old=GUI.color;GUI.color=Color.white;
   // Match runtime altitude ordering globally: all bodies, then rims, then edges.
   // Compositing a complete corner at once would erase its neighbour's rim.
   for(int face=0;face<3;face++)foreach(var c in hullPreview){float x=c.Cell.x+(c.X>0?0:-1),z=c.Cell.z+(c.Z>0?0:-1);var rect=new Rect(canvasOffset.x+x*zoom,canvasOffset.y+(Map.Height-z-2)*zoom,2*zoom,2*zoom);if(rect.xMax<0||rect.yMax<0||rect.x>width||rect.y>height)continue;GUI.DrawTexture(rect,HullPreviewTexture(c,face));}
   GUI.color=old;
  }
 }
}
