using System;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public partial class Workspace
    {
        bool minimapDragging;
        Vector2 minimapGrab;
        Texture2D minimapTexture;int minimapRevision=-1;bool minimapRoofs,minimapAux;
        void DrawMinimapTexture(Rect rect){
            if(minimapTexture==null||minimapRevision!=terrainPreviewRevision||minimapRoofs!=showRoofs||minimapAux!=showAux){
                if(minimapTexture==null||minimapTexture.width!=Map.Width||minimapTexture.height!=Map.Height){
                    if(minimapTexture!=null)UnityEngine.Object.Destroy(minimapTexture);
                    minimapTexture=new Texture2D(Map.Width,Map.Height){filterMode=FilterMode.Point};
                }
                var pixels=terrainPreview.GetPixels();
                void Paint(CellRect bounds,Color color){
                    for(int z=Math.Max(0,bounds.minZ);z<=Math.Min(Map.Height-1,bounds.maxZ);z++)
                        for(int x=Math.Max(0,bounds.minX);x<=Math.Min(Map.Width-1,bounds.maxX);x++)pixels[z*Map.Width+x]=color;
                }
                if(showRoofs)foreach(var p in Map.Objects.Where(p=>p.Kind=="Roof"))Paint(new CellRect(p.Position.X,p.Position.Z,1,1),new Color(.5f,.5f,.7f));
                foreach(var p in Map.Objects){
                    if(p.Kind=="Terrain"||p.Kind=="Roof"||!showAux&&p.Kind=="Marker")continue;
                    var side=MapFactions.Relation(Map,"Player",p.Faction);
                    Paint(PlacementBounds(p),p.Kind=="Marker"?Color.yellow:side=="Hostile"?new Color(1,.25f,.2f):side=="Ally"?new Color(.2f,.9f,.55f):new Color(.95f,.8f,.3f));
                }
                minimapTexture.SetPixels(pixels);minimapTexture.Apply();
                minimapRevision=terrainPreviewRevision;minimapRoofs=showRoofs;minimapAux=showAux;
            }
            GUI.DrawTexture(rect,minimapTexture);
        }

        static Rect MinimapPanel(Rect canvas)
        {
            float width=Math.Min(220,canvas.width*.42f),height=Math.Min(250,canvas.height*.45f);
            return new Rect(canvas.x+10,canvas.yMax-height-10,width,height);
        }

        bool DrawMinimap(Rect panel)
        {
            var e=Event.current;
            bool over=panel.Contains(e.mousePosition),busy=panning||dragging||boxSelecting||surfacePainting;
            if(over&&painting)FinishPaint();
            float scale=Math.Min(Math.Max(1,panel.width-20)/Map.Width,Math.Max(1,panel.height-68)/Map.Height);
            var area=new Rect(panel.x+(panel.width-Map.Width*scale)/2,panel.y+31+(panel.height-68-Map.Height*scale)/2,Map.Width*scale,Map.Height*scale);
            var viewport=new Rect(area.x+mapView.VisibleLeft*scale,area.y+mapView.VisibleTop*scale,
                (mapView.VisibleRight-mapView.VisibleLeft)*scale,(mapView.VisibleBottom-mapView.VisibleTop)*scale);
            if(e.type==EventType.Repaint) {
                Widgets.DrawBoxSolid(panel,new Color(.035f,.055f,.075f,.98f));
                Border(panel,new Color(.25f,.55f,.65f),1);
                EditorUI.DrawLabel(new Rect(panel.x+10,panel.y+4,panel.width-20,25),RimCampaign.Runtime.RcText.T("小地图  ")+Map.Width+" × "+Map.Height);
                Widgets.DrawBoxSolid(area,new Color(.27f,.30f,.22f));
                DrawMinimapTexture(area);
                foreach(var p in HighlightedPlacements().Where(p=>p.Kind!="Terrain"&&p.Kind!="Roof")){
                    var point=new Rect(area.x+(p.Position.X+.5f)*scale-2,area.y+(Map.Height-p.Position.Z-.5f)*scale-2,4,4);
                    Widgets.DrawBoxSolid(ClipToMap(point,area),SelectionColor);
                }
                if(showAux) {
                    foreach(var region in Map.Regions)Border(ClipToMap(new Rect(area.x+region.Min.X*scale,area.y+(Map.Height-1-region.Max.Z)*scale,
                        (region.Max.X-region.Min.X+1)*scale,(region.Max.Z-region.Min.Z+1)*scale),area),new Color(.3f,.65f,1f),1);
                    foreach(var rule in Map.Rules.Where(TriggerGeometry.Spatial))Widgets.DrawBoxSolid(ClipToMap(new Rect(area.x+(rule.Position.X+.5f)*scale-2,area.y+(Map.Height-rule.Position.Z-.5f)*scale-2,4,4),area),Color.yellow);
                }
                Border(area,new Color(.35f,.45f,.5f),1);
                Widgets.DrawBoxSolid(viewport,new Color(1,1,1,.07f));
                Border(viewport,Color.white,2);
            }

            bool enabled=GUI.enabled;
            GUI.enabled=enabled&&!busy&&!minimapDragging;
            if(EditorUI.Button(new Rect(panel.x+8,panel.yMax-30,(panel.width-20)/2,24),RimCampaign.Runtime.RcText.T("居中")))mapView.Center();
            if(EditorUI.Button(new Rect(panel.center.x+2,panel.yMax-30,(panel.width-20)/2,24),RimCampaign.Runtime.RcText.T("全图")))mapView.Fit();
            GUI.enabled=enabled;
            TooltipHandler.TipRegion(panel,RimCampaign.Runtime.RcText.T("白框表示当前视窗；点击定位，拖动白框移动视角。\n绿色：友方；红色：敌方；黄色：中立或辅助对象。"));
            if(busy)return false;

            if(minimapDragging) {
                if((e.type==EventType.MouseUp||e.rawType==EventType.MouseUp)&&e.button==0){minimapDragging=false;e.Use();}
                else if(e.type==EventType.MouseDrag&&e.button==0) {
                    mapView.LookAt((e.mousePosition.x-area.x)/scale+minimapGrab.x,(e.mousePosition.y-area.y)/scale+minimapGrab.y);
                    e.Use();
                }
                return true;
            }
            if(!over)return false;
            if(e.type==EventType.MouseDown&&e.button==0&&area.Contains(e.mousePosition)) {
                var point=new Vector2((e.mousePosition.x-area.x)/scale,(e.mousePosition.y-area.y)/scale);
                minimapGrab=viewport.Contains(e.mousePosition)?new Vector2(mapView.CenterX-point.x,mapView.CenterY-point.y):Vector2.zero;
                mapView.LookAt(point.x+minimapGrab.x,point.y+minimapGrab.y);
                minimapDragging=true;e.Use();
            }else if(e.type==EventType.ScrollWheel) {
                mapView.ZoomAt(e.delta.y,mapView.ViewWidth/2,mapView.ViewHeight/2);e.Use();
            }else if(e.type==EventType.MouseDown||e.type==EventType.MouseDrag||e.type==EventType.MouseUp)e.Use();
            return true;
        }

        static Rect ClipToMap(Rect rect,Rect area)
        {
            float x=Math.Max(rect.x,area.x),y=Math.Max(rect.y,area.y);
            return new Rect(x,y,Math.Max(0,Math.Min(rect.xMax,area.xMax)-x),Math.Max(0,Math.Min(rect.yMax,area.yMax)-y));
        }
        static void Border(Rect rect,Color color,float thickness)
        {
            if(rect.width<=0||rect.height<=0)return;
            float t=Math.Min(thickness,Math.Min(rect.width,rect.height));
            Widgets.DrawBoxSolid(new Rect(rect.x,rect.y,rect.width,t),color);
            Widgets.DrawBoxSolid(new Rect(rect.x,rect.yMax-t,rect.width,t),color);
            Widgets.DrawBoxSolid(new Rect(rect.x,rect.y,t,rect.height),color);
            Widgets.DrawBoxSolid(new Rect(rect.xMax-t,rect.y,t,rect.height),color);
        }
    }
}

