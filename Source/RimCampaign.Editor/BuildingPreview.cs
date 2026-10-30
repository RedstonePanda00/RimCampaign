using System.Collections.Generic;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public partial class Workspace
    {
        bool showRoofs;
        readonly Dictionary<int,LinkFlags> graphicLinks=new Dictionary<int,LinkFlags>();
        int graphicLinksRevision;
        void EnsureGraphicLinks(){EnsureSceneIndex();if(!graphicLinksDirty)return;RebuildGraphicLinks();graphicLinksDirty=false;}
        void RebuildGraphicLinks(){
            graphicLinksRevision++;
            graphicLinks.Clear();
            RebuildHullPreview();
            foreach(var p in Map.Objects)if(p.Kind=="Thing"){
                var def=DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name);
                if(def?.graphicData==null||def.graphicData.linkFlags==LinkFlags.None)continue;
                foreach(var cell in TerrainInitializer.Footprint(p)){
                    int key=cell.Z*Map.Width+cell.X;graphicLinks.TryGetValue(key,out var previous);graphicLinks[key]=previous|def.graphicData.linkFlags;
                }
            }
        }
        public static LinkDirections PreviewLinks(MapSpec map,Placement p,ThingDef def,Dictionary<int,LinkFlags> grid){
            int mask=0;
            for(int i=0;i<4;i++){
                var delta=GenAdj.CardinalDirections[i];int x=p.Position.X+delta.x,z=p.Position.Z+delta.z;
                bool outside=x<0||z<0||x>=map.Width||z>=map.Height;
                if(outside?(def.graphicData.linkFlags&LinkFlags.MapEdge)!=0:grid.TryGetValue(z*map.Width+x,out var flags)&&(flags&def.graphicData.linkFlags)!=0)mask|=1<<i;
            }
            return (LinkDirections)mask;
        }
        void DrawThingGraphic(Placement p,ThingDef def,Rect footprint,float opacity=1){
            if(def.category!=ThingCategory.Building||def.graphicData==null){Widgets.DefIcon(footprint,def);return;}
            var stuff=PlacementStorage.StuffFor(def,p.Stuff);
            var graphic=PlacementGraphics.Preview(p,def);
            var rot=new Rot4(p.Rotation);
            var material=graphic is Graphic_Linked linked
                ?MaterialAtlasPool.SubMaterialFromAtlas(linked.SubGraphic.MatSingle,PreviewLinks(Map,p,def,graphicLinks))
                :graphic.MatAt(rot);
            var texture=material.mainTexture;
            if(texture==null)return;
            bool linkedGraphic=graphic is Graphic_Linked;
            var drawSize=linkedGraphic?Vector2.one:graphic.drawSize;
            if(!linkedGraphic&&rot.IsHorizontal&&!graphic.ShouldDrawRotated)drawSize=new Vector2(drawSize.y,drawSize.x);
            var offset=graphic.DrawOffset(rot);
            var center=footprint.center+new Vector2(offset.x,-offset.z)*zoom;
            var rect=new Rect(center.x-drawSize.x*zoom*.5f,center.y-drawSize.y*zoom*.5f,drawSize.x*zoom,drawSize.y*zoom);
            var scale=material.mainTextureScale;var origin=material.mainTextureOffset;
            var uv=new Rect(origin.x,origin.y,scale.x,scale.y);
            if(!linkedGraphic&&((rot==Rot4.West&&graphic.WestFlipped)||(rot==Rot4.East&&graphic.EastFlipped))){uv.x+=uv.width;uv.width=-uv.width;}
            var matrix=GUI.matrix;var color=GUI.color;
            try{
                GUI.color=(def.MadeFromStuff?def.GetColorForStuff(stuff):graphic.Color)*new Color(1,1,1,opacity);
                if(!linkedGraphic&&graphic.ShouldDrawRotated)GUIUtility.RotateAroundPivot(rot.AsAngle+graphic.DrawRotatedExtraAngleOffset,center);
                GUI.DrawTextureWithTexCoords(rect,texture,uv,true);
            }finally{GUI.matrix=matrix;GUI.color=color;}
        }
    }
}
