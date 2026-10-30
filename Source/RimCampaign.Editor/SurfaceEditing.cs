using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public partial class Workspace
    {
        string editMode="Edit",surfaceShape="Rectangle";
        bool surfacePainting;Cell surfaceStart,surfaceEnd;Placement surfaceBrush;
        List<Cell> surfaceCells=new List<Cell>();readonly List<CellRect> surfaceRuns=new List<CellRect>();
        static readonly Dictionary<string,string> SurfaceShapes=new Dictionary<string,string>{{"Rectangle",RimCampaign.Runtime.RcText.T("长方形")},{"Ellipse",RimCampaign.Runtime.RcText.T("实心圆 / 椭圆")},{"Ring",RimCampaign.Runtime.RcText.T("空心圆 / 椭圆")},{"HollowRectangle",RimCampaign.Runtime.RcText.T("空心长方形")}};
        static bool SurfaceTool(Placement p)=>p!=null&&(p.Kind=="Terrain"||p.Kind=="EraseTerrain"||p.Kind=="EraseFloor");
        void SetEditMode(string value){
            if(surfacePainting)CancelSurfaceStroke();
            if(painting)FinishPaint();
            if(dragging&&tool!="Region")FinishSelectionMove();
            editMode=value;category=value=="Edit"?"Thing":value=="Floor"?"Floor":"Terrain";brush=null;tool="";selection.Clear();selectedId="";assetsScroll=Vector2.zero;
            terrainPreviewDirty=true;EditorFocus.Release();
            status=value=="Edit"?RimCampaign.Runtime.RcText.T("编辑模式：地板与地形不参与选取。"):value=="Floor"?RimCampaign.Runtime.RcText.T("地板模式：只编辑上层地板，保留底层地形。"):RimCampaign.Runtime.RcText.T("地形模式：隐藏上层地板，编辑底层地形。");
        }
        void BeginSurfaceStroke(Placement template,Cell cell){
            surfacePainting=true;surfaceBrush=Json.Copy(template);surfaceStart=Json.Copy(cell);surfaceEnd=null;selection.Clear();selectedId="";UpdateSurfaceStroke(cell);
        }
        void UpdateSurfaceStroke(Cell cell){
            if(surfaceEnd!=null&&surfaceEnd.X==cell.X&&surfaceEnd.Z==cell.Z)return;
            surfaceEnd=Json.Copy(cell);surfaceCells=SurfaceBrush.Cells(surfaceStart,surfaceEnd,Map.Width,Map.Height,surfaceShape);
            surfaceRuns.Clear();
            foreach(var row in surfaceCells.GroupBy(c=>c.Z)){
                int start=-1,last=-1;
                foreach(var point in row){
                    if(start<0)start=point.X;
                    else if(point.X!=last+1){surfaceRuns.Add(new CellRect(start,row.Key,last-start+1,1));start=point.X;}
                    last=point.X;
                }
                if(start>=0)surfaceRuns.Add(new CellRect(start,row.Key,last-start+1,1));
            }
        }
        void CommitSurfaceStroke(){
            if(!surfacePainting)return;
            Edit(_=>TerrainSurfaces.Apply(Map,surfaceBrush,surfaceCells));status=RimCampaign.Runtime.RcText.T("已应用 ")+surfaceCells.Count+RimCampaign.Runtime.RcText.T(" 格；可一次撤销。");CancelSurfaceStroke();
        }
        void CancelSurfaceStroke(){surfacePainting=false;surfaceCells.Clear();surfaceRuns.Clear();surfaceBrush=null;}
        void DrawSurfaceStroke(){
            if(!surfacePainting)return;
            foreach(var run in surfaceRuns){
                var rect=new Rect(canvasOffset.x+run.minX*zoom,canvasOffset.y+(Map.Height-1-run.minZ)*zoom,run.Width*zoom,zoom);
                Widgets.DrawBoxSolid(rect,new Color(.2f,.8f,1f,.38f));
            }
        }
    }
}
