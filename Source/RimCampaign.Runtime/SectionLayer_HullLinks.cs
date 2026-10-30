using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Runtime {
 // Opt-in connections; ordinary wall rendering remains the native linked atlas.
 public sealed class HullLinkExtension:DefModExtension {
  public string linkGroup;
  public float inset=.15f, rimWidth=.10f, frontRimWidth=.19f;
  public Color surfaceColor=Color.white, rimColor=Color.white, edgeColor=Color.gray;
 }
 public readonly struct HullCorner {
  public readonly IntVec3 Cell;
  public readonly int X,Z;
  public readonly bool Full;
  public readonly HullLinkExtension Style;
  public HullCorner(IntVec3 cell,int x,int z,bool full,HullLinkExtension style){Cell=cell;X=x;Z=z;Full=full;Style=style;}
  public Vector2 Transform(Vector2 v)=>new Vector2(Cell.x+(X>0?v.x:1-v.x),Cell.z+(Z>0?v.y:1-v.y));
 }
 public static class HullConnections {
  // Odyssey's empty-cell/eight-neighbour decision, using opt-in groups instead
  // of its hard-coded GravshipHull identity. Full corners include a diagonal
  // wall; partial corners pair with the opposite half to form a diagonal run.
  public static bool TryCorner(IntVec3 cell,Func<IntVec3,ThingDef> edifice,Func<IntVec3,bool> substructure,out HullCorner corner){
   corner=default;if(edifice(cell)!=null||substructure(cell))return false;
   var n=edifice(cell+IntVec3.North);var e=edifice(cell+IntVec3.East);var s=edifice(cell+IntVec3.South);var w=edifice(cell+IntVec3.West);
   foreach(var source in new[]{n,e,s,w}){
    var style=source?.GetModExtension<HullLinkExtension>();if(style==null)continue;
    bool Match(ThingDef d){var other=d?.GetModExtension<HullLinkExtension>();return other!=null&&(d==source||!string.IsNullOrEmpty(style.linkGroup)&&style.linkGroup==other.linkGroup);}
    bool nn=Match(n),ee=Match(e),ss=Match(s),ww=Match(w);int x,z;
    if(nn&&ww&&!ss&&!ee){x=-1;z=1;}
    else if(nn&&ee&&!ss&&!ww){x=1;z=1;}
    else if(ss&&ee&&!nn&&!ww){x=1;z=-1;}
    else if(ss&&ww&&!nn&&!ee){x=-1;z=-1;}
    else continue;
    corner=new HullCorner(cell,x,z,Match(edifice(cell+new IntVec3(x,0,z))),style);return true;
   }
   return false;
  }
  public static bool TryCorner(Map map,IntVec3 cell,out HullCorner corner)=>TryCorner(cell,p=>p.InBounds(map)?p.GetEdifice(map)?.def:null,p=>p.InBounds(map)&&(map.terrainGrid.FoundationAt(p)?.IsSubstructure??false),out corner);
  // Bridge whole cells and cover the old square rims where the two walls join.
  public static IEnumerable<(Vector2[] Points,Color Color)> Faces(HullCorner c){
   var style=c.Style;float a=style.inset,b=1+a;
   var p=new Vector2(a,b);var q=new Vector2(b,a);
   var body=new List<Vector2>{p,new Vector2(a+style.rimWidth,1.5f),new Vector2(1.5f,1.5f),new Vector2(1.5f,a+style.rimWidth),q};
   // A partial connection must stop at the opposite side of the diagonal run.
   // Full corners can fill up to the existing diagonal wall instead.
   if(!c.Full){var clipped=new List<Vector2>();float limit=3-2*a;for(int i=0;i<body.Count;i++){var from=body[i];var to=body[(i+1)%body.Count];bool inside=from.x+from.y<=limit,next=to.x+to.y<=limit;if(inside)clipped.Add(from);if(inside!=next){float t=(limit-from.x-from.y)/(to.x+to.y-from.x-from.y);clipped.Add(Vector2.Lerp(from,to,t));}}body=clipped;}
   yield return (body.ConvertAll(c.Transform).ToArray(),style.surfaceColor);
   float width=(c.Z>0?style.frontRimWidth:style.rimWidth)/Mathf.Sqrt(2f);
   var shift=new Vector2(width,width);
   yield return (new[]{c.Transform(p),c.Transform(p+shift),c.Transform(q+shift),c.Transform(q)},style.rimColor);
   var edge=new Vector2(.025f,.025f);
   yield return (new[]{c.Transform(p),c.Transform(p+edge),c.Transform(q+edge),c.Transform(q)},style.edgeColor);
  }
 }
 public sealed class SectionLayer_HullLinks:SectionLayer {
  public SectionLayer_HullLinks(Section section):base(section){relevantChangeTypes=(ulong)MapMeshFlagDefOf.Buildings|(ulong)MapMeshFlagDefOf.Things|(ulong)MapMeshFlagDefOf.Terrain;}
  public override void Regenerate(){
   ClearSubMeshes(MeshParts.All);
   ThingDef Edifice(IntVec3 p)=>p.InBounds(Map)?p.GetEdifice(Map)?.def:null;
   bool Foundation(IntVec3 p)=>p.InBounds(Map)&&(Map.terrainGrid.FoundationAt(p)?.IsSubstructure??false);
   Func<IntVec3,ThingDef> edifice=Edifice;Func<IntVec3,bool> foundation=Foundation;
   foreach(var cell in section.CellRect){
    if(!HullConnections.TryCorner(cell,edifice,foundation,out var corner))continue;
    int face=0;
    foreach(var part in HullConnections.Faces(corner)){
     var mesh=GetSubMesh(MaterialPool.MatFrom(BaseContent.WhiteTex,ShaderDatabase.Cutout,part.Color));int first=mesh.verts.Count;
     float y=AltitudeLayer.BuildingOnTop.AltitudeFor()+face++*.001f;
     foreach(var p in part.Points){mesh.verts.Add(new Vector3(p.x,y,p.y));mesh.uvs.Add(Vector3.zero);mesh.colors.Add(Color.white);}
     for(int i=1;i<part.Points.Length-1;i++){mesh.tris.Add(first);mesh.tris.Add(first+(corner.X*corner.Z>0?i:i+1));mesh.tris.Add(first+(corner.X*corner.Z>0?i+1:i));}
    }
   }
   FinalizeMesh(MeshParts.All);
  }
  public override CellRect GetBoundaryRect()=>section.CellRect.ExpandedBy(1);
 }
}
