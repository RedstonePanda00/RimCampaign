using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RimCampaign.Core
{
    public class DependencyReport
    {
        public List<Dependency> Direct { get; set; } = new List<Dependency>();
        public List<string> LoadAfter { get; set; } = new List<string>();
        public Dictionary<string,List<string>> Uses { get; set; } = new Dictionary<string,List<string>>();
        public List<string> Errors { get; set; } = new List<string>();
    }
    public static class Dependencies
    {
        public static DependencyReport Scan(Project p, IEnumerable<Dependency> installed)
        {
            var result = new DependencyReport();
            var catalog = installed.GroupBy(d => d.PackageId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var direct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Use(string package, string location) {
                if (string.IsNullOrWhiteSpace(package)) { result.Errors.Add("Unresolved dependency at " + location); return; }
                package = package.ToLowerInvariant();
                if (package == "ludeon.rimworld" || string.Equals(package,p.PackageId,StringComparison.OrdinalIgnoreCase)) return;
                direct.Add(package);
                if (!result.Uses.ContainsKey(package)) result.Uses[package] = new List<string>();
                result.Uses[package].Add(location);
            }
            void CommunicationDependencies(Act action,string location) {
                var c=action.Communication;if(c==null)return;
                if(!string.IsNullOrEmpty(c.Portrait?.Name)&&!string.Equals(c.Portrait.Package,p.PackageId,StringComparison.OrdinalIgnoreCase))Use(c.Portrait.Package,location+"/Portrait/"+c.Portrait.Name);
                foreach(var package in new[]{c.PortraitPackage,c.RendererPackage})if(!string.IsNullOrEmpty(package)&&!string.Equals(package,p.PackageId,StringComparison.OrdinalIgnoreCase))Use(package,location+"/Communication");
            }
            Use("rimcampaign.framework", "Framework");
            foreach(var structure in p.Structures??new List<StructureSpec>())
                foreach(var reference in Structures.References(structure))Use(reference.Package,"Structure/"+structure.Name+"/"+reference.Name);
            foreach (var m in p.Maps) {
                foreach(var a in Validation.Actions(m.Entry.Concat(m.Defeat).Concat(m.Rules.SelectMany(r=>r.Actions)).Concat(m.Groups.SelectMany(g=>g.Actions)).Concat(m.Interactions.SelectMany(i=>i.Actions))))
                {
                    if(a.SoundPulse?.Sound!=null&&!string.IsNullOrEmpty(a.SoundPulse.Sound.Name))Use(a.SoundPulse.Sound.Package,m.Name+"/SoundPulse/"+a.SoundPulse.Sound.Name);
                    CommunicationDependencies(a,m.Name);
                }
                foreach(var device in m.Rewards??new List<MapRewardSpec>())foreach(var loot in device.Contents??new List<DeviceLoot>()) {
                    if(loot.Def!=null)Use(loot.Def.Package,m.Name+"/"+device.Name+"/Contents");
                    if(loot.Stuff!=null)Use(loot.Stuff.Package,m.Name+"/"+device.Name+"/Stuff");
                }
                Use(m.Terrain.Package, m.Name + "/Terrain");
                if(m.TerrainInitialization!=null) {
                    Use(m.TerrainInitialization.Biome.Package,m.Name+"/Biome");
                    if(m.TerrainInitialization.SecondaryBiome!=null) {
                        Use(m.TerrainInitialization.SecondaryBiome.Package,m.Name+"/SecondaryBiome");
                        Use("ludeon.rimworld.odyssey",m.Name+"/MixedBiome");
                    }
                }
                foreach (var o in m.Objects.Where(o => o.Kind != "Marker" && o.Kind != "Pawn" && o.Kind != "Boss")) {
                    Use(o.Def.Package, m.Name + "/" + o.Name);
                    if (o.Stuff != null) Use(o.Stuff.Package, m.Name + "/" + o.Name + "/Stuff");
                    foreach(var loot in o.Contents??new List<DeviceLoot>()){
                        if(loot.Def!=null)Use(loot.Def.Package,m.Name+"/"+o.Name+"/Contents");
                        if(loot.Stuff!=null)Use(loot.Stuff.Package,m.Name+"/"+o.Name+"/Contents/Stuff");
                    }
                }
            }
            foreach (var c in p.Campaigns) foreach (var n in c.Nodes) foreach (var o in n.Resources) {
                Use(o.Def.Package, c.Name + "/" + n.Name + "/Resources");
                if (o.Stuff != null) Use(o.Stuff.Package, c.Name + "/" + n.Name + "/Stuff");
                foreach(var loot in o.Contents??new List<DeviceLoot>()){
                    if(loot.Def!=null)Use(loot.Def.Package,c.Name+"/"+n.Name+"/Contents");
                    if(loot.Stuff!=null)Use(loot.Stuff.Package,c.Name+"/"+n.Name+"/Contents/Stuff");
                }
            }
            foreach (var a in p.Profiles) foreach (var r in a.References) Use(r.Package, "Profiles/" + a.Name + "/" + r.Name);
            foreach (var a in p.Profiles.Where(a => a.GenerateFrom != null)) Use(a.GenerateFrom.Package, "Generated/" + a.Name);
            foreach(var boss in p.Bosses??new List<BossTemplate>()) {
                foreach(var component in boss.Components??new List<BossComponentSpec>())foreach(var exception in component.DamageExceptions??new List<DefRef>())Use(exception.Package,"Boss/"+boss.Name+"/DamageExceptions");
                foreach(var a in Validation.Actions(boss.Phases.SelectMany(f=>f.OnEnter)))CommunicationDependencies(a,"Boss/"+boss.Name);
                if(string.IsNullOrEmpty(boss.Profile)&&boss.PawnKind!=null)Use(boss.PawnKind.Package,"Boss/"+boss.Name);
                foreach(var action in boss.Phases.SelectMany(f=>f.Behaviors))if(action.Ability!=null)Use(action.Ability.Package,"Boss/"+boss.Name+"/Ability");
            }
            foreach (var r in p.ExtraReferences) Use(r.Package, "Custom/" + r.Name);
            foreach (var d in p.ManualDependencies) Use(d.PackageId, "Manual");
            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ludeon.rimworld" };
            void Resolve(string id) {
                if (!required.Add(id)) return;
                if (!catalog.TryGetValue(id, out var dep)) { result.Errors.Add("Missing dependency: " + id); return; }
                foreach (var child in dep.Requires) Resolve(child.ToLowerInvariant());
            }
            foreach (var id in direct) Resolve(id);
            foreach (var id in direct.OrderBy(x => x)) if (catalog.TryGetValue(id, out var dep)) result.Direct.Add(dep);
            var order = new HashSet<string>(required, StringComparer.OrdinalIgnoreCase);
            order.UnionWith(p.ManualLoadAfter.Select(x => x.ToLowerInvariant()));
            if (order.Contains(p.PackageId)) result.Errors.Add("Campaign cannot depend on or load after itself.");
            order.Remove(p.PackageId);
            result.LoadAfter = order.OrderBy(x => x).ToList();
            // A -> B means A must be loaded after B; detect conflicts using known installed metadata.
            var graph = new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in order.Concat(new[] { p.PackageId })) graph[id] = new List<string>();
            graph[p.PackageId].AddRange(order);
            foreach (var d in catalog.Values.Where(d => graph.ContainsKey(d.PackageId))) {
                graph[d.PackageId].AddRange(d.After.Concat(d.Requires).Where(graph.ContainsKey));
                foreach (var before in d.Before.Where(graph.ContainsKey)) graph[before].Add(d.PackageId);
            }
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool Visit(string id) {
                if (active.Contains(id)) return true; if (done.Contains(id)) return false;
                active.Add(id); bool cycle = graph[id].Any(Visit); active.Remove(id); done.Add(id); return cycle;
            }
            if (graph.Keys.Any(Visit)) result.Errors.Add("Dependency/loadAfter/loadBefore cycle.");
            return result;
        }
    }
    public static class Exporter
    {
        public static string Export(Project p, string parent, DependencyReport report, string assetRoot = null)
        {
            var errors = Validation.Check(p).Select(x => x.ToString()).Concat(report.Errors).ToList();
            if (p.Campaigns.Count == 0) errors.Add("Add a campaign before export.");
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            // Write a fresh sibling staging tree; never partially replace a previously working package.
            string root = Path.Combine(Path.GetFullPath(parent), p.PackageId);
            string staging = root + ".staging-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.Combine(staging, "About"));
            Directory.CreateDirectory(Path.Combine(staging, "1.7", "Defs"));
            Directory.CreateDirectory(Path.Combine(staging, "1.7", "Campaigns"));
            var meta = new XElement("ModMetaData", new XElement("name", p.Name), new XElement("author", p.Author),
                new XElement("packageId", p.PackageId), new XElement("supportedVersions", new XElement("li", "1.7")),
                new XElement("modDependencies", report.Direct.Select(d => new XElement("li", new XElement("packageId", d.PackageId), new XElement("displayName", d.Name)))),
                new XElement("loadAfter", report.LoadAfter.Select(id => new XElement("li", id))),
                new XElement("description", "Created with RimCampaign."));
            new XDocument(meta).Save(Path.Combine(staging, "About", "About.xml"));
            new XDocument(new XElement("loadFolders", new XElement("v1.7", new XElement("li", "/"), new XElement("li", "1.7")))).Save(Path.Combine(staging, "LoadFolders.xml"));
            Json.Save(Path.Combine(staging, "1.7", "Campaigns", "project.json.gz"), p);
            var defs = new XElement("Defs");
            foreach(var structure in p.Structures??new List<StructureSpec>()) {
                Structures.Validate(structure);
                string relative="Structures/"+Structures.DefName(structure)+".rcstructure.json";
                Json.Save(Path.Combine(staging,"1.7",relative),structure);
                defs.Add(Structures.Definition(structure,relative));
            }
            foreach (var c in p.Campaigns) {
                string defName = "RC_" + c.Id.Replace("-", "_");
                defs.Add(new XElement("RimCampaign.Runtime.CampaignDef", new XElement("defName", defName), new XElement("label", c.Name),
                    new XElement("projectFile", "Campaigns/project.json.gz"), new XElement("campaignId", c.Id)));
                defs.Add(new XElement("ScenarioDef", new XAttribute("ParentName","ScenarioBase"), new XElement("defName", defName + "_Scenario"), new XElement("label", c.Name),
                    new XElement("description", p.Name), new XElement("scenario", new XElement("name", c.Name),
                    new XElement("description", p.Name), new XElement("playerFaction", new XAttribute("Class", "ScenPart_PlayerFaction"),
                    new XElement("def", "PlayerFaction"), new XElement("factionDef", "PlayerColony")),
                    new XElement("parts", new XElement("li", new XAttribute("Class", "RimCampaign.Runtime.ScenPart_Campaign"),
                    new XElement("def", "RC_CampaignPart"), new XElement("campaign", defName))))));
            }
            new XDocument(defs).Save(Path.Combine(staging, "1.7", "Defs", "Campaigns.xml"));
            foreach (var assembly in p.CustomAssemblies) {
                if (!Path.GetFileName(assembly).StartsWith(p.PackageId + ".", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Custom assembly must be named " + p.PackageId + ".*.dll");
                Directory.CreateDirectory(Path.Combine(staging, "1.7", "Assemblies"));
                File.Copy(assembly, Path.Combine(staging, "1.7", "Assemblies", Path.GetFileName(assembly)));
            }
            if (assetRoot != null) foreach (var folder in new[] { "Textures", "Languages", "Sounds" }) {
                var source = Path.Combine(assetRoot, folder); if (!Directory.Exists(source)) continue;
                foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) {
                    var target = Path.Combine(staging, folder, file.Substring(source.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target);
                }
            }
            if (Directory.Exists(root)) {
                var backup = root + ".backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                Directory.Move(root, backup);
                try { Directory.Move(staging, root); } catch { Directory.Move(backup, root); throw; }
            } else Directory.Move(staging, root);
            return root;
        }
    }
}
