using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.Text;

namespace RimCampaign.Core
{
    public static class Json
    {
        static DataContractJsonSerializer Serializer<T>() => new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        public static string Write<T>(T value) { using (var s = new MemoryStream()) { Serializer<T>().WriteObject(s, value); return Encoding.UTF8.GetString(s.ToArray()); } }
        public static T Read<T>(string text) { using (var s = new MemoryStream(Encoding.UTF8.GetBytes(text))) return (T)Serializer<T>().ReadObject(s); }
        public static T Copy<T>(T value) => Read<T>(Write(value));
        public static void Save<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var tmp = path + ".tmp"; File.WriteAllText(tmp, Write(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }
        public static T Load<T>(string path) => Read<T>(File.ReadAllText(path, Encoding.UTF8));
    }
    public class Named
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
    }
    public class DefRef
    {
        public string Kind { get; set; } = "ThingDef";
        public string Name { get; set; } = "";
        public string Package { get; set; } = "ludeon.rimworld";
    }
    public class Project : Named
    {
        public int Version { get; set; } = 1;
        public string PackageId { get; set; } = "rimcampaign.mycampaign";
        public string Author { get; set; } = "";
        public List<MapSpec> Maps { get; set; } = new List<MapSpec>();
        public List<Campaign> Campaigns { get; set; } = new List<Campaign>();
        public List<Profile> Profiles { get; set; } = new List<Profile>();
        public List<BossTemplate> Bosses { get; set; } = new List<BossTemplate>();
        public List<Dependency> ManualDependencies { get; set; } = new List<Dependency>();
        public List<string> ManualLoadAfter { get; set; } = new List<string>();
        public List<DefRef> ExtraReferences { get; set; } = new List<DefRef>();
        public List<string> CustomAssemblies { get; set; } = new List<string>();
        [OnDeserialized]
        void RestoreOptionalCollections(StreamingContext context) { if(Bosses==null)Bosses=new List<BossTemplate>(); }
    }
    public class Cell { public int X { get; set; } public int Z { get; set; } }
    public class Placement : Named
    {
        public bool SpawnAtStart { get; set; } = true;
        [OnDeserializing] void DefaultPlacement(StreamingContext context){SpawnAtStart=true;}
        public string Kind { get; set; } = "Thing";
        public DefRef Def { get; set; } = new DefRef();
        public DefRef Stuff { get; set; }
        public Cell Position { get; set; } = new Cell();
        public int Rotation { get; set; }
        public int Count { get; set; } = 1;
        public string Role { get; set; } = "";
        public string Group { get; set; } = "";
        public string Faction { get; set; } = "Player";
        public string Layer { get; set; } = "Objects";
        public string Boss { get; set; } = "";
        public string BindRole { get; set; } = "";
    }
    public class Region : Named { public Cell Min { get; set; } = new Cell(); public Cell Max { get; set; } = new Cell(); }
    public class MapFaction : Named { public string Side { get; set; } = "Neutral"; }
    public class MapFactionRelation
    {
        public string A { get; set; } = "Player";
        public string B { get; set; } = "Hostile";
        public string Kind { get; set; } = "Hostile";
    }
    public static class MapFactions
    {
        public static List<MapFaction> Defaults()=>new List<MapFaction>{new MapFaction{Id="Player",Name="阵营 1（玩家）",Side="Ally"},new MapFaction{Id="Hostile",Name="阵营 2",Side="Hostile"}};
        public static string Relation(MapSpec map,string a,string b) {
            if(a==b)return "Ally";
            string Side(string id)=>id=="Player"?"Ally":map.Factions.Find(f=>f.Id==id)?.Side??"Neutral";
            string first=Side(a),second=Side(b);
            if(first=="Neutral"||second=="Neutral")return "Neutral";
            return first==second?"Ally":"Hostile";
        }
        public static void SetSide(MapSpec map,string id,string side) {
            if(side!="Ally"&&side!="Hostile"&&side!="Neutral")throw new ArgumentException("Unknown faction side.");
            if(id=="Player"&&side!="Ally")throw new ArgumentException("The player remains on the allied side.");
            var faction=map.Factions.Find(f=>f.Id==id)??throw new ArgumentException("Unknown map faction.");
            faction.Side=side;
        }
    }

    public class MapSpec : Named
    {
        public List<MapFaction> Factions { get; set; } = MapFactions.Defaults();
        // Legacy pairwise data is consumed on load, never used for runtime relationships.
        public List<MapFactionRelation> FactionRelations { get; set; } = new List<MapFactionRelation>();
        [OnDeserialized]
        void RestoreFactions(StreamingContext context) {
            if(Factions==null)Factions=MapFactions.Defaults();
            foreach(var faction in Factions) {
                if(faction.Id=="Player"){faction.Side="Ally";continue;}
                if(faction.Side==null)faction.Side=FactionRelations?.Find(r=>(r.A=="Player"&&r.B==faction.Id)||(r.B=="Player"&&r.A==faction.Id))?.Kind??"Neutral";
            }
            FactionRelations=new List<MapFactionRelation>();
        }
        public int Width { get; set; } = 80;
        public int Height { get; set; } = 80;
        public string Base { get; set; } = "Blank";
        public DefRef Terrain { get; set; } = new DefRef { Kind = "TerrainDef", Name = "Soil" };
        public List<Placement> Objects { get; set; } = new List<Placement>();
        public List<Region> Regions { get; set; } = new List<Region>();
        public List<string> Roles { get; set; } = new List<string> { "hero" };
        public Dictionary<string, double> Inputs { get; set; } = new Dictionary<string, double>();
        public List<string> Outputs { get; set; } = new List<string> { "success", "failure" };
        public List<Rule> Rules { get; set; } = new List<Rule>();
        public List<Objective> Objectives { get; set; } = new List<Objective>();
        public List<HudWidget> Hud { get; set; } = new List<HudWidget>();
        public List<ActionGroup> Groups { get; set; } = new List<ActionGroup>();
        public List<Act> Entry { get; set; } = new List<Act>();
        public List<Act> Defeat { get; set; } = new List<Act>();
    }
    public class Profile : Named
    {
        // When present, generate fresh randomized data from this loaded PawnKind for each new actor.
        public DefRef GenerateFrom { get; set; }
        public string Payload { get; set; } = "";
        public List<DefRef> References { get; set; } = new List<DefRef>();
        public List<string> PortabilityErrors { get; set; } = new List<string>();
        public List<RelationRef> Relations { get; set; } = new List<RelationRef>();
    }
    public class BossTemplate : Named
    {
        public double MaxHealth { get; set; } = 3000;
        public DefRef PawnKind { get; set; } = new DefRef { Kind="PawnKindDef", Name="Colonist" };
        public string Profile { get; set; } = "";
        public List<BossPhase> Phases { get; set; } = new List<BossPhase> { new BossPhase { Name="阶段 1" } };
    }
    public class BossPhase : Named
    {
        public bool LockHealth { get; set; }
        public bool AutoUnlock { get; set; } = true;
        public List<Act> OnEnter { get; set; } = new List<Act>();
        [OnDeserializing] void DefaultPhase(StreamingContext context){AutoUnlock=true;OnEnter=new List<Act>();}
        public double HealthFraction { get; set; } = 1;
        public List<BossAction> Behaviors { get; set; } = new List<BossAction> {
            new BossAction { Module="boss.approach", Range=2, CooldownTicks=60 },
            new BossAction { Module="boss.weapon", Range=30, CooldownTicks=90 }
        };
    }
    public class BossAction : Named
    {
        public string Module { get; set; } = "boss.weapon";
        public int CooldownTicks { get; set; } = 90;
        public float Range { get; set; } = 20;
        public float Power { get; set; } = 20;
        public DefRef Ability { get; set; }
        public Dictionary<string,string> Parameters { get; set; } = new Dictionary<string,string>();
    }
    public class RelationRef {
        public string Relation { get; set; } = "";
        public string TargetProfile { get; set; } = "";
        public string TargetName { get; set; } = "";
    }
    public class Objective : Named
    {
        public string Mode { get; set; } = "Standard";
        public string Role { get; set; } = "";
        public bool Required { get; set; } = true;
    }
    public class HudWidget : Named
    {
        public string Kind { get; set; } = "Text";
        public string Binding { get; set; } = "";
        public string Text { get; set; } = "";
        public float X { get; set; } = 16; public float Y { get; set; } = 100;
        public float Width { get; set; } = 250; public float Height { get; set; } = 32;
        public double Maximum { get; set; } = 100;
    }
    public class Condition
    {
        public string Op { get; set; } = "Always";
        public string Key { get; set; } = "";
        public double Value { get; set; }
        public List<Condition> Children { get; set; } = new List<Condition>();
    }
    public class EventSpec
    {
        public string Kind { get; set; } = "Entry";
        public string Target { get; set; } = "";
        public double Seconds { get; set; }
    }
    public class Rule : Named
    {
        public string Host { get; set; } = "";
        public float Radius { get; set; } = 5;
        public double DamageWindowSeconds { get; set; } = 5;
        public double DamageThreshold { get; set; } = 50;
        [OnDeserializing] void DefaultRule(StreamingContext context){Host="";Radius=5;DamageWindowSeconds=5;DamageThreshold=50;}
        public Cell Position { get; set; } = new Cell();
        public bool Enabled { get; set; } = true;
        public bool Once { get; set; } = true;
        public List<EventSpec> Events { get; set; } = new List<EventSpec> { new EventSpec() };
        public Condition When { get; set; } = new Condition();
        public List<Act> Actions { get; set; } = new List<Act>();
    }
    public class Act : Named
    {
        public string Kind { get; set; } = "Wait";
        public string Target { get; set; } = "";
        public string Text { get; set; } = "";
        public double Value { get; set; }
        public bool Freeze { get; set; }
        public Condition When { get; set; } = new Condition();
        public List<Act> Steps { get; set; } = new List<Act>();
        public List<Act> Else { get; set; } = new List<Act>();
    }
    public class ActionGroup : Named { public List<Act> Actions { get; set; } = new List<Act>(); }
    public class Campaign : Named
    {
        public string Start { get; set; } = "";
        public Dictionary<string, double> Variables { get; set; } = new Dictionary<string, double>();
        public List<Node> Nodes { get; set; } = new List<Node>();
        public List<Edge> Edges { get; set; } = new List<Edge>();
    }
    public class Node : Named
    {
        public string Kind { get; set; } = "Story";
        public string Map { get; set; } = "";
        public float X { get; set; } public float Y { get; set; }
        public string Arrival { get; set; } = "Position";
        public bool Inherit { get; set; } = true;
        public Dictionary<string, string> Roles { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, double> Inputs { get; set; } = new Dictionary<string, double>();
        public List<Placement> Resources { get; set; } = new List<Placement>();
        public bool Hunger { get; set; } = true; public bool Mood { get; set; } = true;
        public bool Work { get; set; } = true; public bool Construction { get; set; } = true;
        public bool Research { get; set; } = true; public bool Storyteller { get; set; } = true;
    }
    public class Edge : Named
    {
        public string From { get; set; } = ""; public string To { get; set; } = "";
        public string Result { get; set; } = "success"; public int Priority { get; set; }
        public Condition When { get; set; } = new Condition();
    }
    public class Dependency
    {
        public string PackageId { get; set; } = ""; public string Name { get; set; } = "";
        public List<string> Requires { get; set; } = new List<string>();
        public List<string> After { get; set; } = new List<string>();
        public List<string> Before { get; set; } = new List<string>();
    }
}
