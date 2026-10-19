using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime
{
    // Separate actual factions per authored map: changing one map never changes another's teams.
    public sealed class CampaignFactionRegistry : GameComponent
    {
        Dictionary<string,Faction> factions=new Dictionary<string,Faction>();
        HashSet<string> initialized=new HashSet<string>();
        HashSet<string> grouped=new HashSet<string>();
        List<string> keys;List<Faction> values;
        public CampaignFactionRegistry(Game game){}
        public override void ExposeData() {
            Scribe_Collections.Look(ref factions,"rcMapFactions",LookMode.Value,LookMode.Reference,ref keys,ref values);
            Scribe_Collections.Look(ref initialized,"rcInitializedFactionMaps",LookMode.Value);
            Scribe_Collections.Look(ref grouped,"rcGroupedFactionMaps",LookMode.Value);
            if(Scribe.mode==LoadSaveMode.PostLoadInit){factions=factions??new Dictionary<string,Faction>();initialized=initialized??new HashSet<string>();grouped=grouped??new HashSet<string>();}
        }
        public override void LoadedGame() {
            var project=Current.Game.GetComponent<CampaignController>()?.Project;
            if(project!=null)foreach(var map in project.Maps.Where(m=>initialized.Contains(m.Id)&&!grouped.Contains(m.Id)))Ensure(map);
        }
        public Faction Resolve(MapSpec map,string id) {
            if(id=="Neutral")return null; // Legacy unowned placements remain unowned.
            if(!map.Factions.Any(f=>f.Id==id))throw new InvalidOperationException("Unknown map faction: "+id);
            Ensure(map);
            return id=="Player"?Faction.OfPlayer:factions[map.Id+"/"+id];
        }
        void Ensure(MapSpec map) {
            if(initialized.Contains(map.Id)&&grouped.Contains(map.Id))return;
            foreach(var team in map.Factions.Where(f=>f.Id!="Player")) {
                string key=map.Id+"/"+team.Id;
                if(factions.TryGetValue(key,out var existing)&&existing!=null)continue;
                var faction=FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(DefDatabase<FactionDef>.GetNamed("RC_MapFaction")));
                faction.Name=team.Name;
                // No settlements, raids or diplomacy changes between existing world factions.
                foreach(var other in Find.FactionManager.AllFactionsListForReading)
                    faction.SetRelation(new FactionRelation(other,FactionRelationKind.Neutral){baseGoodwill=0});
                Find.FactionManager.Add(faction);factions[key]=faction;
            }
            Faction Get(string id)=>id=="Player"?Faction.OfPlayer:factions[map.Id+"/"+id];
            for(int i=0;i<map.Factions.Count;i++)for(int j=i+1;j<map.Factions.Count;j++) {
                var a=map.Factions[i];var b=map.Factions[j];
                var kind=(FactionRelationKind)Enum.Parse(typeof(FactionRelationKind),MapFactions.Relation(map,a.Id,b.Id));
                var first=Get(a.Id);var second=Get(b.Id);
                int goodwill=kind==FactionRelationKind.Ally?100:kind==FactionRelationKind.Hostile?-100:0;
                first.SetRelation(new FactionRelation(second,kind){baseGoodwill=goodwill});
                second.RelationWith(first).baseGoodwill=goodwill;
            }
            initialized.Add(map.Id);grouped.Add(map.Id);
        }
    }
}
