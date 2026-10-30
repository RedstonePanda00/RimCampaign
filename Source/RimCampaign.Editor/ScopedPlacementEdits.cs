using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using Verse;

namespace RimCampaign.Editor {
    public partial class Workspace {
        public sealed class PlacedRecord {public int Index{get;set;}public Placement Value{get;set;}}
        public sealed class RuleRecord {public int Index{get;set;}public Rule Value{get;set;}}
        public sealed class PlacementScope {public List<PlacedRecord> Objects{get;set;}public List<RuleRecord> Rules{get;set;}}
        void EditPlacements(IEnumerable<Placement> changed,IEnumerable<string> ruleIds,Action change,bool clearNature){
            var values=changed.ToList();var objectIds=new HashSet<string>(values.Select(p=>p.Id));var rules=new HashSet<string>(ruleIds);
            var cells=clearNature?values.Where(p=>!TerrainInitializer.Generated(p)&&!TerrainInitializer.NaturalObject(p)&&p.Kind!="Marker"&&p.Kind!="Roof"&&p.Kind!="EditorTrigger").SelectMany(TerrainInitializer.Footprint).ToList():new List<Cell>();
            var cellKeys=new HashSet<int>(cells.Select(c=>c.Z*Map.Width+c.X));
            // Include nature and ground that the paste/rotation may clear, so undo
            // restores it too. Unrelated maps and terrain are never serialized.
            foreach(var p in Map.Objects)if(cellKeys.Contains(p.Position.Z*Map.Width+p.Position.X))objectIds.Add(p.Id);
            bool terrain=values.Any(p=>p.Kind=="Terrain")||Map.Objects.Any(p=>cellKeys.Contains(p.Position.Z*Map.Width+p.Position.X)&&p.Kind=="Terrain"&&p.Def.Name.EndsWith("_Rough"));
            string id=Map.Id;
            MapSpec Resolve(Project p)=>p.Maps.First(m=>m.Id==id);
            PlacementScope Capture(Project p){var m=Resolve(p);var result=new PlacementScope{Objects=new List<PlacedRecord>(),Rules=new List<RuleRecord>()};
                for(int i=0;i<m.Objects.Count;i++)if(objectIds.Contains(m.Objects[i].Id))result.Objects.Add(new PlacedRecord{Index=i,Value=m.Objects[i]});
                for(int i=0;i<m.Rules.Count;i++)if(rules.Contains(m.Rules[i].Id))result.Rules.Add(new RuleRecord{Index=i,Value=m.Rules[i]});
                return result;}
            void Restore(Project p,PlacementScope s){var m=Resolve(p);m.Objects.RemoveAll(v=>objectIds.Contains(v.Id));m.Rules.RemoveAll(v=>rules.Contains(v.Id));
                foreach(var v in s.Objects.OrderBy(v=>v.Index))m.Objects.Insert(Math.Min(v.Index,m.Objects.Count),v.Value);
                foreach(var v in s.Rules.OrderBy(v=>v.Index))m.Rules.Insert(Math.Min(v.Index,m.Rules.Count),v.Value);
                PatrolAuthoring.Synchronize(m);
            }
            try{history.EditScope(Capture,Restore,p=>{change();if(clearNature)TerrainInitializer.ClearNatureAt(Map,cells);PatrolAuthoring.Synchronize(Map);});}
            finally{InvalidateScene(terrain);}
        }
        static bool HasEditableFaction(Placement p)=>p.Kind=="Pawn"||p.Kind=="PawnKind"||p.Kind=="Boss"||
            p.Kind=="Thing"&&(DefDatabase<ThingDef>.GetNamedSilentFail(p.Def?.Name)?.category==ThingCategory.Building||DefDatabase<ThingDef>.GetNamedSilentFail(p.Def?.Name)?.CanHaveFaction==true);
        void PickSelectionFaction()=>EditorUI.Pick(Map.Factions.Select(f=>new KeyValuePair<string,string>(f.Id,f.Name)),SetSelectionFaction);
        void SetSelectionFaction(string faction){
            if(!Map.Factions.Any(f=>f.Id==faction))return;
            var selected=SelectedPlacements();var eligible=selected.Where(HasEditableFaction).ToList();var changed=eligible.Where(p=>p.Faction!=faction).ToList();
            if(changed.Count>0)EditPlacements(changed,Array.Empty<string>(),()=>{foreach(var p in changed)p.Faction=faction;},false);
            status=RcText.T("已批量修改阵营：")+changed.Count+RcText.T("；跳过不支持阵营的对象：")+(SelectedEditable().Count-eligible.Count)+RcText.T("。可撤销。");
        }
    }
}
