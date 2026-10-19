using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimCampaign.Core;

namespace RimCampaign.SDK
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CampaignModuleAttribute : Attribute
    {
        public string Id { get; } public string Category { get; } public string Sentence { get; }
        public string Version { get; set; } = "1";
        public CampaignModuleAttribute(string id, string category, string sentence) { Id = id; Category = category; Sentence = sentence; }
    }
    public interface IActionModule { bool Execute(Act action, IHost host, string source, double delta); }
    public interface IConditionModule { bool Evaluate(Condition condition, IHost host); }
    public interface IDataSourceModule { double Read(string key, IHost host); }
    public interface IArrivalModule { void Arrive(object pawn, object map, Cell destination); }
    public interface IObjectAdapter { string Capture(object value); object Restore(string data); IEnumerable<DefRef> References(string data); }
    public interface IEventModule { IEnumerable<Signal> Poll(IHost host, double gameDelta); }
    // State is persisted per encounter and behavior. Module objects themselves must be stateless.
    public interface IBossContext
    {
        object Pawn { get; } object Target { get; } object Map { get; }
        double Health { get; } double MaxHealth { get; } int Phase { get; } int Tick { get; }
        IDictionary<string,string> State { get; }
        bool Approach(float stopRange);
        bool FireWeapon(float range);
        bool CastAbility(string defName, float range);
        bool DamageTarget(float damage, float range);
    }
    public interface IBossBehaviorModule { bool Execute(BossAction action, IBossContext context); }
    public interface IBossPawnImportModule { void Import(object sourcePawn, object independentCopy); }
    public sealed class ModuleDescriptor
    {
        public string Id; public string Category; public string Sentence; public string Version; public Type Type;
    }
    public static class Modules
    {
        static readonly Dictionary<string,ModuleDescriptor> entries = new Dictionary<string,ModuleDescriptor>();
        public static IEnumerable<ModuleDescriptor> All => entries.Values;
        public static void Discover(Assembly assembly)
        {
            // Attribute metadata is inspected without instantiating plugin classes or attributes.
            foreach (var type in assembly.GetTypes().Where(t => !t.IsAbstract)) {
                var a = CustomAttributeData.GetCustomAttributes(type).FirstOrDefault(x => x.AttributeType == typeof(CampaignModuleAttribute));
                if (a == null) continue;
                var id = (string)a.ConstructorArguments[0].Value;
                if (entries.ContainsKey(id)) throw new InvalidOperationException("Duplicate module ID: " + id);
                entries.Add(id, new ModuleDescriptor { Id = id, Category = (string)a.ConstructorArguments[1].Value,
                    Sentence = (string)a.ConstructorArguments[2].Value, Version = "1", Type = type });
            }
        }
        public static object Create(string id) => entries.TryGetValue(id, out var d) ? Activator.CreateInstance(d.Type) : null;
    }
}
