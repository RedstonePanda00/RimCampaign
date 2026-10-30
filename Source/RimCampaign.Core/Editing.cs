using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RimCampaign.Core
{
    public sealed class History<T>
    {
        sealed class Snapshot {
            public byte[] Data;
            public Func<T,string> Capture;
            public Func<T,string,T> Apply;
        }
        readonly LinkedList<Snapshot> undo = new LinkedList<Snapshot>(), redo = new LinkedList<Snapshot>();
        readonly int maxEntries;readonly long maxBytes;
        public long RetainedBytes { get; private set; }
        public int UndoCount=>undo.Count;
        public int RedoCount=>redo.Count;
        public T Value { get; private set; }
        public History(T value):this(value,100,64*1024*1024){}
        public History(T value,int maxEntries=100,long maxBytes=64*1024*1024) { Value=value;this.maxEntries=Math.Max(1,maxEntries);this.maxBytes=Math.Max(1,maxBytes); }
        static byte[] Pack(string value){
            using(var output=new MemoryStream()){
                using(var gzip=new GZipStream(output,CompressionLevel.Fastest,true))
                using(var writer=new StreamWriter(gzip,Encoding.UTF8))writer.Write(value);
                return output.ToArray();
            }
        }
        static string Unpack(byte[] value){using(var input=new MemoryStream(value))using(var gzip=new GZipStream(input,CompressionMode.Decompress))using(var reader=new StreamReader(gzip,Encoding.UTF8))return reader.ReadToEnd();}
        void Clear(LinkedList<Snapshot> list){foreach(var item in list)RetainedBytes-=item.Data.Length;list.Clear();}
        void RemoveFirst(LinkedList<Snapshot> list){RetainedBytes-=list.First.Value.Data.Length;list.RemoveFirst();}
        void Push(LinkedList<Snapshot> list,Snapshot value){
            list.AddLast(value);RetainedBytes+=value.Data.Length;
            while(undo.Count+redo.Count>maxEntries||RetainedBytes>maxBytes){
                // The oldest states are at the front; keep the next undo/redo
                // whenever it fits, including after switching direction.
                var victim=list==undo?(undo.Count>1?undo:redo):(redo.Count>1?redo:undo);
                if(victim.Count==0)victim=list;
                RemoveFirst(victim);
            }
        }
        void Record(string before){Clear(redo);Push(undo,new Snapshot{Data=Pack(before),Capture=v=>Json.Write(v),Apply=(v,s)=>Json.Read<T>(s)});}
        // Small editor operations can snapshot only their affected records. Restore
        // resolves them against the current document, so full and scoped undo mix.
        public void EditScope<TScope>(Func<T,TScope> capture,Action<T,TScope> restore,Action<T> edit){
            string before=Json.Write(capture(Value));
            try{edit(Value);}catch{restore(Value,Json.Read<TScope>(before));throw;}
            if(Json.Write(capture(Value))==before)return;
            Clear(redo);Push(undo,new Snapshot{Data=Pack(before),Capture=v=>Json.Write(capture(v)),Apply=(v,s)=>{restore(v,Json.Read<TScope>(s));return v;}});
        }
        public void Replace(T value) { Record(Json.Write(Value));Value=value; }
        public void Edit(Action<T> edit) {
            string before = Json.Write(Value);
            try { edit(Value); } catch { Value = Json.Read<T>(before); throw; }
            if (Json.Write(Value) != before) Record(before);
        }
        void Restore(LinkedList<Snapshot> from,LinkedList<Snapshot> to){
            if(from.Count==0)return;
            var entry=from.Last.Value;
            var current=new Snapshot{Data=Pack(entry.Capture(Value)),Capture=entry.Capture,Apply=entry.Apply};
            Value=entry.Apply(Value,Unpack(entry.Data));
            RetainedBytes-=entry.Data.Length;from.RemoveLast();Push(to,current);
        }
        public void Undo()=>Restore(undo,redo);
        public void CommitAppliedChange(string before) { if(before!=null&&Json.Write(Value)!=before)Record(before); }
        public void Redo()=>Restore(redo,undo);
        public static List<Placement> CopyObjects(IEnumerable<Placement> objects, int dx, int dz) {
            var copy = Json.Copy(objects.ToList());
            foreach (var p in copy) { p.Id = Guid.NewGuid().ToString("N"); p.Position.X += dx; p.Position.Z += dz; }
            return copy;
        }
    }
    public class Issue { public string Path { get; set; } public string Message { get; set; } public override string ToString() => Path + ": " + Message; }
    public static class Validation
    {
        public static List<Issue> Check(Project p)
        {
            var issues = new List<Issue>();
            void Error(string path, string message) => issues.Add(new Issue { Path = path, Message = message });
            if (p.Version != 1) Error("Version", "Unsupported project version.");
            foreach(var structure in p.Structures??new List<StructureSpec>())try{Structures.Validate(structure);}catch(Exception e){Error("Structure/"+structure.Id,e.Message);}
            foreach(var duplicate in (p.Structures??new List<StructureSpec>()).GroupBy(s=>s.Id).Where(g=>g.Count()>1))Error(duplicate.Key,"Duplicate structure ID.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(p.PackageId ?? "", @"^[a-zA-Z0-9_-]+(\.[a-zA-Z0-9_-]+)+$")) Error("PackageId", "Use a stable dotted package ID.");
            if (p.PackageId == "rimcampaign.framework") Error("PackageId", "Campaign must have its own package ID.");
            foreach (var duplicate in p.Maps.Cast<Named>().Concat(p.Profiles).Concat(p.Bosses??new List<BossTemplate>()).Concat(p.Campaigns).GroupBy(x => x.Id).Where(g => g.Count() > 1)) Error(duplicate.Key, "Duplicate asset ID.");
            foreach (var profile in p.Profiles) {
                foreach (var error in profile.PortabilityErrors) Error(profile.Id, error);
                if (profile.GenerateFrom == null && string.IsNullOrWhiteSpace(profile.Payload)) Error(profile.Id, "Character profile is empty.");
                if (profile.GenerateFrom != null && (profile.GenerateFrom.Kind != "PawnKindDef" || string.IsNullOrWhiteSpace(profile.GenerateFrom.Name))) Error(profile.Id, "Select a PawnKindDef for random generation.");
                if (profile.References.Any(r => r.Package.Equals("void.charactereditor", StringComparison.OrdinalIgnoreCase))) Error(profile.Id, "Profile still references Character Editor content.");
                foreach(var relation in profile.Relations)
                    if(!p.Profiles.Any(x=>x.Id==relation.TargetProfile)||relation.TargetProfile==profile.Id)Error(profile.Id,"Bind relation "+relation.Relation+" to another profile.");
            }
            foreach(var boss in p.Bosses??new List<BossTemplate>()) {
                var components=boss.Components??new List<BossComponentSpec>();
                if(components.GroupBy(c=>c.Id).Any(g=>g.Count()>1))Error(boss.Id,"Duplicate boss component IDs.");
                foreach(var component in components){
                    if(string.IsNullOrWhiteSpace(component.Module))Error(boss.Id,"Boss component needs a module.");
                    if((component.DamageExceptions??new List<DefRef>()).Any(d=>d.Kind!="DamageDef"||string.IsNullOrWhiteSpace(d.Name)))Error(boss.Id,"Damage exceptions must reference DamageDefs.");
                    if(component.Module=="boss.adaptation"){
                        double Read(string key,double fallback)=>component.Parameters!=null&&component.Parameters.TryGetValue(key,out var value)?double.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var n)?n:double.NaN:fallback;
                        try{BossAdaptation.Compress(1,Read("threshold",100),Read("ceiling",500));}catch(ArgumentException){Error(boss.Id,"Adaptation requires finite 0 <= threshold < ceiling.");}
                    }
                }
                if(double.IsNaN(boss.MaxHealth)||double.IsInfinity(boss.MaxHealth)||boss.MaxHealth<=0)Error(boss.Id,"Boss health must be finite and positive.");
                if(boss.Phases.Count==0||boss.Phases[0].HealthFraction!=1)Error(boss.Id,"Boss needs an initial phase at health fraction 1.");
                double previous=2;
                foreach(var phase in boss.Phases) {
                    if(double.IsNaN(phase.HealthFraction)||phase.HealthFraction<=0||phase.HealthFraction>1||phase.HealthFraction>=previous)Error(boss.Id,"Boss phase fractions must strictly descend within (0, 1].");
                    previous=phase.HealthFraction;
                    if(phase.Behaviors.Select(a=>a.Id).Distinct().Count()!=phase.Behaviors.Count)Error(boss.Id,"Duplicate boss behavior IDs.");
                    foreach(var action in phase.Behaviors) {
                        if(string.IsNullOrWhiteSpace(action.Module)||action.CooldownTicks<1||float.IsNaN(action.Range)||float.IsInfinity(action.Range)||action.Range<0||float.IsNaN(action.Power)||float.IsInfinity(action.Power)||action.Power<0)Error(boss.Id,"Invalid boss behavior parameters.");
                        if(action.Module=="boss.ability"&&(action.Ability?.Kind!="AbilityDef"||string.IsNullOrWhiteSpace(action.Ability.Name)))Error(boss.Id,"Native ability behavior needs an AbilityDef.");
                    }
                }
                if(string.IsNullOrEmpty(boss.Profile)&&(boss.PawnKind?.Kind!="PawnKindDef"||string.IsNullOrWhiteSpace(boss.PawnKind.Name)))Error(boss.Id,"Select a fallback PawnKind for the boss.");
                if(!string.IsNullOrEmpty(boss.Profile)&&!p.Profiles.Any(a=>a.Id==boss.Profile))Error(boss.Id,"Missing boss source profile.");
            }
            foreach (var m in p.Maps) {
                foreach(var error in TerrainNoise.Errors(m.TerrainInitialization))Error(m.Id+"/TerrainInitialization",error);
                issues.AddRange(StealthMath.Validate(m));
                if(m.Rewards.GroupBy(r=>r.Id).Any(g=>g.Count()>1))Error(m.Id,"Map reward IDs must be unique.");
                foreach(var reward in m.Rewards){
                    if(!string.IsNullOrEmpty(reward.StockpileMarker)&&!m.Objects.Any(o=>o.Id==reward.StockpileMarker))Error(m.Id+"/"+reward.Id,"Unknown reward stockpile marker.");
                    if(reward.Contents.Count==0||reward.Contents.Any(l=>l.Def==null||string.IsNullOrEmpty(l.Def.Name)||l.Def.Kind!="ThingDef"||l.Count<1||l.Count>10000))Error(m.Id+"/"+reward.Id,"Invalid generated reward contents.");
                    if(!string.IsNullOrEmpty(reward.Recipient)&&!m.Roles.Contains(reward.Recipient))Error(m.Id+"/"+reward.Id,"Unknown reward recipient role.");
                }
                if(m.Components.Any(c=>string.IsNullOrWhiteSpace(c.Type))||m.Components.GroupBy(c=>c.Type).Any(g=>g.Count()>1))Error(m.Id,"Map component types must be nonempty and unique.");
                if(m.Interactions.GroupBy(i=>i.Id).Any(g=>g.Count()>1))Error(m.Id,"Interaction IDs must be unique.");
                foreach(var interaction in m.Interactions){
                    if(!m.Objects.Any(o=>o.Id==interaction.Host&&o.Kind=="Thing")||interaction.UseTicks<1)Error(m.Id+"/"+interaction.Id,"Interaction requires an existing building and positive use duration.");
                    if(interaction.BlockCells.Any(at=>at.X<0||at.Z<0||at.X>=m.Width||at.Z>=m.Height)||interaction.BlockCells.Count>0&&string.IsNullOrEmpty(interaction.ObstacleDef))Error(m.Id+"/"+interaction.Id,"Invalid obstacle cells or definition.");
                    CheckCondition(interaction.When,interaction.Id,Error);
                }
                foreach(var route in m.PatrolRoutes){
                    if(route.Points.Count==0||route.Points.Any(p=>p.Position.X<0||p.Position.Z<0||p.Position.X>=m.Width||p.Position.Z>=m.Height||p.WaitTicks<0))Error(m.Id+"/"+route.Id,"Invalid combat patrol route.");
                }
                foreach(var binding in m.CombatPatrols)
                    if(!m.Objects.Any(o=>o.Id==binding.Pawn&&new[]{"Pawn","PawnKind","Boss"}.Contains(o.Kind))||!m.PatrolRoutes.Any(r=>r.Id==binding.Route)||binding.Radius<=0||binding.Radius>100)Error(m.Id+"/"+binding.Id,"Invalid combat patrol binding.");
                if(m.CombatPatrols.GroupBy(p=>p.Pawn).Any(g=>g.Count()>1))Error(m.Id,"A pawn may have only one combat patrol binding.");
                if(m.Factions.Count(f=>f.Id=="Player")!=1)Error(m.Id,"Map needs exactly one Player faction.");
                if(m.Factions.Any(f=>string.IsNullOrWhiteSpace(f.Id)||f.Id=="Neutral"||string.IsNullOrWhiteSpace(f.Name))||m.Factions.GroupBy(f=>f.Id).Any(g=>g.Count()>1))Error(m.Id,"Invalid or duplicate map faction IDs/names.");
                if(m.Factions.Any(f=>!new[]{"Ally","Hostile","Neutral"}.Contains(f.Side)||(f.Id=="Player"&&f.Side!="Ally")))Error(m.Id,"Invalid map faction side.");
                if (m.Width < 20 || m.Height < 20 || m.Width > 500 || m.Height > 500) Error(m.Id, "Map dimensions must be 20..500.");
                var named = m.Objects.Cast<Named>().Concat(m.Regions).Concat(m.Rules).Concat(m.Objectives).Concat(m.Groups).ToList();
                foreach(var objective in m.Objectives)CheckCondition(objective.VisibleWhen, objective.Id, Error);
                foreach (var g in named.GroupBy(n => n.Id).Where(g => g.Count() > 1)) Error(m.Id + "/" + g.Key, "Duplicate object ID.");
                foreach (var o in m.Objects) {
                    if(o.GraphicIndex<0)Error(m.Id+"/"+o.Id,"Graphic variant index must be non-negative.");
                    if(o.Contents?.Any(l=>l.Def==null||string.IsNullOrEmpty(l.Def.Name)||l.Def.Kind!="ThingDef"||l.Count<1||l.Count>10000||l.Stuff!=null&&l.Stuff.Kind!="ThingDef")==true)Error(m.Id+"/"+o.Id,"Invalid building contents.");
                    if(o.Faction!="Neutral"&&!m.Factions.Any(f=>f.Id==o.Faction))Error(m.Id+"/"+o.Id,"Unknown map faction.");
                    if (o.Position.X < 0 || o.Position.Z < 0 || o.Position.X >= m.Width || o.Position.Z >= m.Height) Error(m.Id + "/" + o.Id, "Object outside map.");
                    if (o.Kind == "Pawn" && !m.Roles.Contains(o.Role)) Error(m.Id + "/" + o.Id, "Unknown role: " + o.Role);
                    if (o.Kind == "PawnKind" && (o.Def?.Kind != "PawnKindDef" || string.IsNullOrWhiteSpace(o.Def.Name))) Error(m.Id + "/" + o.Id, "PawnKind placement needs a PawnKindDef.");
                    if(o.Kind=="Boss"&&!(p.Bosses??new List<BossTemplate>()).Any(b=>b.Id==o.Boss))Error(m.Id+"/"+o.Id,"Missing Boss template.");
                    if(o.Kind=="Boss"&&!string.IsNullOrEmpty(o.BindRole)&&!m.Roles.Contains(o.BindRole))Error(m.Id+"/"+o.Id,"Unknown Boss binding role.");
                }
                foreach (var r in m.Regions.Concat(m.NaturalClearings))
                    if (r.Min.X > r.Max.X || r.Min.Z > r.Max.Z || r.Min.X < 0 || r.Min.Z < 0 || r.Max.X >= m.Width || r.Max.Z >= m.Height) Error(m.Id + "/" + r.Id, "Invalid region.");
                foreach (var r in m.Rules) {
                    if(r.Events.Any(e=>e.Kind=="Periodic"&&(double.IsNaN(e.Seconds)||double.IsInfinity(e.Seconds)||e.Seconds<=0)))Error(m.Id+"/"+r.Id,"Periodic interval must be finite and positive.");
                    if(!new[]{"Circle","Rectangle"}.Contains(r.RangeShape)||float.IsNaN(r.RangeWidth)||float.IsInfinity(r.RangeWidth)||r.RangeWidth<=0||r.RangeWidth>2000||float.IsNaN(r.RangeHeight)||float.IsInfinity(r.RangeHeight)||r.RangeHeight<=0||r.RangeHeight>2000)Error(m.Id+"/"+r.Id,"Invalid trigger shape or dimensions.");
                    if(!new[]{"PlayerUnits","AnyPawn","Hostile","Friendly","Specific"}.Contains(r.Detect))Error(m.Id+"/"+r.Id,"Unknown trigger subject filter.");
                    if(r.Detect=="Specific"&&!m.Objects.Any(o=>o.Id==r.Subject&&new[]{"Pawn","PawnKind","Boss"}.Contains(o.Kind))&&!m.Roles.Any(role=>r.Subject=="role:"+role))Error(m.Id+"/"+r.Id,"Missing specific trigger subject.");
                    if(TriggerGeometry.Visible(r)&&(r.Position==null||r.Position.X<0||r.Position.Z<0||r.Position.X>=m.Width||r.Position.Z>=m.Height))Error(m.Id+"/"+r.Id,"Spatial trigger outside map.");
                    if(r.Events.Any(e=>e.Kind=="Password")&&string.IsNullOrEmpty(r.Password))Error(m.Id+"/"+r.Id,"Password trigger requires a nonempty password.");
                    if(!string.IsNullOrEmpty(r.Host)&&!m.Objects.Any(o=>o.Id==r.Host&&new[]{"Pawn","PawnKind","Boss"}.Contains(o.Kind))&&!m.Roles.Any(role=>r.Host=="role:"+role))Error(m.Id+"/"+r.Id,"Unknown trigger host.");
                    if(float.IsNaN(r.Radius)||float.IsInfinity(r.Radius)||r.Radius<=0||r.Radius>1000)Error(m.Id+"/"+r.Id,"Trigger radius must be within (0, 1000].");
                    if(r.Events.Any(e=>e.Kind=="DamageWindow")&&(double.IsNaN(r.DamageWindowSeconds)||double.IsInfinity(r.DamageWindowSeconds)||r.DamageWindowSeconds<=0||r.DamageWindowSeconds>3600||double.IsNaN(r.DamageThreshold)||double.IsInfinity(r.DamageThreshold)||r.DamageThreshold<=0))Error(m.Id+"/"+r.Id,"Invalid trigger damage window/threshold.");
                    if (r.Events.Count == 0) Error(m.Id + "/" + r.Id, "Trigger needs an event.");
                    CheckCondition(r.When, r.Id, Error);
                }
                foreach(var tree in m.Groups)if(!string.IsNullOrEmpty(tree.Actor)&&tree.Actor!="$source"&&!m.Roles.Any(r=>tree.Actor==r||tree.Actor=="role:"+r)&&!m.Objects.Any(o=>o.Id==tree.Actor&&new[]{"Pawn","PawnKind","Boss"}.Contains(o.Kind)))Error(m.Id+"/"+tree.Id,"Missing task actor.");
                var groupGraph = m.Groups.ToDictionary(g => g.Id, g => Actions(g.Actions).Where(a => a.Kind == "Group").Select(a => a.Target).ToList());
                CheckCycles(groupGraph, m.Id, Error);
                foreach (var a in Actions(m.Entry.Concat(m.Defeat).Concat(m.Interactions.SelectMany(i=>i.Actions)).Concat(m.Rules.SelectMany(r => r.Actions)).Concat(m.Groups.SelectMany(g => g.Actions)).Concat((p.Bosses??new List<BossTemplate>()).Where(b=>m.Objects.Any(o=>o.Kind=="Boss"&&o.Boss==b.Id)).SelectMany(b=>b.Phases.SelectMany(f=>f.OnEnter))))) {
                    if(a.Kind=="rc.reinforce"&&!string.IsNullOrEmpty(a.Origin)&&!m.Objects.Any(o=>o.Id==a.Origin&&o.Kind=="Marker")&&!m.Rules.Any(r=>r.Id==a.Origin&&TriggerGeometry.Visible(r)))Error(m.Id+"/"+a.Id,"Missing reinforcement origin marker/trigger.");
                    if(a.Kind=="rc.reinforce"&&a.Arrival!="Ground"&&a.Arrival!="Pods")Error(m.Id+"/"+a.Id,"Unknown reinforcement arrival mode.");
                    if(a.Kind=="rc.spawnStructure"){
                        var structure=p.Structures?.FirstOrDefault(s=>s.Id==a.Target||Structures.DefName(s)==a.Target);
                        if(structure==null&&!p.ExtraReferences.Any(r=>r.Kind=="CampaignStructureDef"&&r.Name==a.Target))Error(m.Id+"/"+a.Id,"Missing referenced structure.");
                        if(!m.Objects.Any(o=>o.Id==a.Origin&&o.Kind=="Marker"))Error(m.Id+"/"+a.Id,"Missing structure center marker.");
                        if(a.Arrival!="Ground"&&a.Arrival!="Pods")Error(m.Id+"/"+a.Id,"Unknown structure arrival mode.");
                        if(a.Arrival=="Pods"&&structure?.Objects.Any(o=>o.Kind=="Terrain"||o.Kind=="Roof")==true)Error(m.Id+"/"+a.Id,"Terrain and roofs cannot arrive in drop pods.");
                        if(!string.IsNullOrEmpty(a.Text)&&!m.Factions.Any(f=>f.Id==a.Text))Error(m.Id+"/"+a.Id,"Unknown structure faction override.");
                    }
                    if(a.Kind=="rc.buildOutpost"&&(!m.Objects.Any(o=>o.Id==a.Origin&&o.Kind=="Marker")||!m.Objects.Any(o=>o.Group==a.Target&&o.Kind=="PawnKind")||!m.Objects.Any(o=>o.Group==a.Text&&o.Kind=="Thing")))Error(m.Id+"/"+a.Id,"Outpost needs engineer slots, blueprint placements and a map marker.");
                    if(a.Kind=="Spawn"&&!m.Objects.Any(o=>o.Id==a.Target))Error(m.Id+"/"+a.Id,"Missing spawn placement.");
                    if(a.Kind=="rc.task.move"&&!m.Objects.Any(o=>o.Id==a.Target&&o.Kind=="Marker"))Error(m.Id+"/"+a.Id,"Task movement needs a map marker.");
                    if(a.Kind=="rc.task.operate"&&!m.Objects.Any(o=>o.Id==a.Target&&o.Kind=="Thing"))Error(m.Id+"/"+a.Id,"Task operation needs a building.");
                    if((a.Kind=="rc.task.engage"||a.Kind=="rc.reinforce"||a.Kind=="rc.defendGroup")&&(string.IsNullOrEmpty(a.Target)||!m.Objects.Any(o=>o.Group==a.Target)))Error(m.Id+"/"+a.Id,"Task needs an existing placement group.");
                    if(a.Kind=="rc.defendGroup"&&!m.Objects.Any(o=>o.Id==a.Text&&o.Kind=="Marker"))Error(m.Id+"/"+a.Id,"Defense needs a map marker.");
                    if(a.Kind=="rc.reinforce"&&((!string.IsNullOrEmpty(a.Text)&&!m.Groups.Any(g=>g.Id==a.Text&&g.Actor=="$source"))||!m.Objects.Any(o=>o.Group==a.Target&&o.Kind=="PawnKind")))Error(m.Id+"/"+a.Id,"Reinforcement needs PawnKind slots and a task tree bound to $source.");
                    if((a.Kind=="rc.task.move"||a.Kind=="rc.task.operate"||a.Kind=="rc.reinforce")&&(double.IsNaN(a.Value)||double.IsInfinity(a.Value)||a.Value<0))Error(m.Id+"/"+a.Id,"Invalid task duration, radius or wave size.");
                    if(a.Kind=="Communication") {
                        var comm=a.Communication??new CommunicationSpec();
                        foreach(var error in comm.Errors())Error(m.Id+"/"+a.Id,error);
                        if(!string.IsNullOrEmpty(comm.Speaker)&&comm.Speaker!="$source"&&!m.Roles.Any(r=>comm.Speaker==r||comm.Speaker=="role:"+r)&&!m.Objects.Any(o=>o.Id==comm.Speaker&&new[]{"Pawn","PawnKind","Boss"}.Contains(o.Kind)))Error(m.Id+"/"+a.Id,"Missing communication speaker.");
                    }
                    if(a.Kind=="SoundPulse"){
                        var sound=a.SoundPulse;
                        bool Exists(string id)=>id=="$source"||m.Objects.Any(o=>o.Id==id)||m.Roles.Any(r=>id==r||id=="role:"+r);
                        if(sound==null||float.IsNaN(sound.Radius)||float.IsInfinity(sound.Radius)||sound.Radius<0||sound.Radius>200||!new[]{"Pawns","Guards","Things"}.Contains(sound.Receivers))Error(m.Id+"/"+a.Id,"Invalid sound pulse settings.");
                        if(!Exists(a.Target))Error(m.Id+"/"+a.Id,"Missing sound origin.");
                        if(sound?.Targets?.Any(id=>!Exists(id))==true)Error(m.Id+"/"+a.Id,"Missing sound recipient.");
                        if(sound?.Sound!=null&&!string.IsNullOrEmpty(sound.Sound.Name)&&sound.Sound.Kind!="SoundDef")Error(m.Id+"/"+a.Id,"Sound pulse audio must reference a SoundDef.");
                        if(!string.IsNullOrEmpty(sound?.Faction)&&!m.Factions.Any(f=>f.Id==sound.Faction))Error(m.Id+"/"+a.Id,"Missing sound recipient faction.");
                    }
                    if(a.Kind=="PasswordPrompt"&&!m.Rules.Any(r=>r.Id==a.Target&&r.Events.Any(e=>e.Kind=="Password")))Error(m.Id+"/"+a.Id,"Password prompt requires a password trigger.");
                    if(a.Kind=="SpawnGroup"&&(string.IsNullOrEmpty(a.Target)||!m.Objects.Any(o=>o.Group==a.Target)))Error(m.Id+"/"+a.Id,"Missing placement group.");
                    if(a.Kind=="RemoveTerrain"&&!m.Objects.Any(o=>o.Id==a.Target)&&!m.Regions.Any(o=>o.Id==a.Target))Error(m.Id+"/"+a.Id,"Missing terrain removal region/placement.");
                    if(a.Kind=="UnlockBoss"&&a.Target!="$source"&&!m.Objects.Any(o=>o.Id==a.Target&&o.Kind=="Boss"))Error(m.Id+"/"+a.Id,"Missing Boss unlock target.");
                    if (a.Value < 0 && a.Kind == "Wait") Error(m.Id + "/" + a.Id, "Wait must be nonnegative.");
                    if (a.Kind == "Group" && !m.Groups.Any(g => g.Id == a.Target)) Error(m.Id + "/" + a.Id, "Missing action group.");
                    if (a.Kind == "Objective" && !m.Objectives.Any(o => o.Id == a.Target)) Error(m.Id + "/" + a.Id, "Missing objective.");
                    if (a.Kind == "Enable" && !m.Rules.Any(r => r.Id == a.Target)) Error(m.Id + "/" + a.Id, "Missing trigger.");
                    CheckCondition(a.When, a.Id, Error);
                }
            }
            foreach (var c in p.Campaigns) {
                if (c.Nodes.Select(n => n.Id).Distinct().Count() != c.Nodes.Count) { Error(c.Id, "Duplicate node IDs."); continue; }
                if (!c.Nodes.Any(n => n.Id == c.Start)) Error(c.Id, "Missing start node.");
                foreach (var n in c.Nodes) {
                    var m = p.Maps.FirstOrDefault(x => x.Id == n.Map);
                    if (m == null && n.Kind != "Open") Error(c.Id + "/" + n.Id, "Missing map.");
                    if(m!=null)foreach(var resource in n.Resources)if(resource.Faction!="Neutral"&&!m.Factions.Any(f=>f.Id==resource.Faction))Error(c.Id+"/"+n.Id,"Unknown resource map faction.");
                    if (m != null) foreach (var role in m.Roles) {
                        if (!n.Roles.TryGetValue(role, out var id) || !p.Profiles.Any(x => x.Id == id)) Error(c.Id + "/" + n.Id + "/" + role, "Bind role to a profile.");
                    }
                }
                foreach (var e in c.Edges) {
                    if (!c.Nodes.Any(n => n.Id == e.From) || !c.Nodes.Any(n => n.Id == e.To)) Error(c.Id + "/" + e.Id, "Missing edge endpoint.");
                    CheckCondition(e.When, e.Id, Error);
                }
                var graph = c.Nodes.ToDictionary(n => n.Id, n => c.Edges.Where(e => e.From == n.Id).Select(e => e.To).ToList());
                CheckCycles(graph, c.Id, Error);
                var seen = new HashSet<string>(); var pending = new Stack<string>(); pending.Push(c.Start);
                while (pending.Count > 0) { var id = pending.Pop(); if (!seen.Add(id) || !graph.ContainsKey(id)) continue; foreach (var next in graph[id]) pending.Push(next); }
                foreach (var n in c.Nodes.Where(n => !seen.Contains(n.Id))) Error(c.Id + "/" + n.Id, "Unreachable node.");
            }
            return issues;
        }
        static void CheckCondition(Condition c, string path, Action<string,string> error) {
            if (c == null) return;
            if (c.Op == "Not" && c.Children.Count != 1) error(path, "NOT requires one condition.");
            foreach (var child in c.Children) CheckCondition(child, path, error);
        }
        public static IEnumerable<Act> Actions(IEnumerable<Act> actions) {
            foreach (var a in actions) { yield return a; foreach (var b in Actions(a.Steps.Concat(a.Else))) yield return b; }
        }
        static void CheckCycles(Dictionary<string,List<string>> graph, string path, Action<string,string> error) {
            var visiting = new HashSet<string>(); var done = new HashSet<string>();
            bool Visit(string id) {
                if (visiting.Contains(id)) return true;
                if (!graph.ContainsKey(id) || done.Contains(id)) return false;
                visiting.Add(id); bool cycle = graph[id].Any(Visit); visiting.Remove(id); done.Add(id); return cycle;
            }
            if (graph.Keys.Any(Visit)) error(path, "Cycle detected.");
        }
    }
}
