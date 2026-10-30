using System.Collections.Generic;
using RimCampaign.Core;

namespace RimCampaign.Editor {
    public partial class Workspace {
        MapSpec indexedMap;
        bool sceneIndexDirty=true,graphicLinksDirty=true;
        int sceneRevision,indexedObjectCount;
        readonly Dictionary<string,Named> sceneById=new Dictionary<string,Named>();
        readonly List<Placement> drawableObjects=new List<Placement>(),roofObjects=new List<Placement>();

        // Editing and gesture previews invalidate derived data. Idle GUI events never scan
        // the map to detect changes. A new Map reference covers undo/redo and project load.
        void InvalidateScene(bool terrain=true){sceneIndexDirty=true;graphicLinksDirty=true;terrainPreviewDirty|=terrain;EditorUI.InvalidateReferences(indexedMap);if(!ReferenceEquals(indexedMap,Map))EditorUI.InvalidateReferences(Map);}
        void EnsureSceneIndex(){
            var map=Map;
            if(!sceneIndexDirty&&ReferenceEquals(indexedMap,map)&&indexedObjectCount==map.Objects.Count)return;
            sceneById.Clear();drawableObjects.Clear();roofObjects.Clear();
            foreach(var p in map.Objects){
                if(!sceneById.ContainsKey(p.Id))sceneById.Add(p.Id,p);
                if(p.Kind=="Roof")roofObjects.Add(p);
                else if(p.Kind!="Terrain")drawableObjects.Add(p);
            }
            void Add<T>(IEnumerable<T> values) where T:Named {foreach(var value in values)if(!sceneById.ContainsKey(value.Id))sceneById.Add(value.Id,value);}
            Add(map.Regions);Add(map.Rules);Add(map.Objectives);Add(map.Hud);Add(map.PatrolRoutes);Add(map.Stealth.Routes);Add(map.Interactions);Add(map.Groups);Add(map.Rewards);
            RebuildRelations();
            indexedMap=map;indexedObjectCount=map.Objects.Count;sceneIndexDirty=false;graphicLinksDirty=true;sceneRevision++;
        }
        Named FindSceneItem(string id){
            if(string.IsNullOrEmpty(id))return null;
            EnsureSceneIndex();return sceneById.TryGetValue(id,out var value)?value:null;
        }
        Placement FindPlacement(string id)=>FindSceneItem(id) as Placement;
        IEnumerable<Placement> HighlightedPlacements(){
            EnsureSceneIndex();
            foreach(var id in selection)if(sceneById.TryGetValue(id,out var value)&&value is Placement p)yield return p;
            if(!string.IsNullOrEmpty(selectedId)&&!selection.Contains(selectedId)&&sceneById.TryGetValue(selectedId,out var selected)&&selected is Placement placed)yield return placed;
        }
    }
}
