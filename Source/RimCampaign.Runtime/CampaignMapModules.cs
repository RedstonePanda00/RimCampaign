using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using Verse;

namespace RimCampaign.Runtime
{
    public interface ICampaignMapModule
    {
        void Attach(CampaignController controller,MapComponentSpec spec);
        void SetEnabled(bool enabled);
        void Detach();
    }
    // Draw before communications, including cinematics which intentionally skip the normal map HUD.
    // Ownership and lifetime remain with the explicitly attached map component.
    public interface ICampaignMapOverlay
    {
        void DrawCampaignOverlay();
    }
    public static class CampaignMapModules
    {
        public static IEnumerable<Type> Available => GenTypes.AllTypes.Where(t=>!t.IsAbstract&&typeof(CustomMapComponent).IsAssignableFrom(t)&&typeof(ICampaignMapModule).IsAssignableFrom(t)&&t.GetConstructor(new[]{typeof(Map)})!=null);
        public static Type Resolve(string name) => Available.FirstOrDefault(t=>t.FullName==name||t.AssemblyQualifiedName==name)
            ?? throw new InvalidOperationException(RimCampaign.Runtime.RcText.T("地图组件必须是实现 ICampaignMapModule 的 CustomMapComponent，且具有 Map 构造函数：")+name);
        public static void Apply(CampaignController c){foreach(var spec in c.MapSpec.Components.ToList())Add(c,spec);}
        public static CustomMapComponent Add(CampaignController c,MapComponentSpec spec) {
            var type=Resolve(spec.Type);
            var existing=c.Map.components.FirstOrDefault(x=>x.GetType()==type) as CustomMapComponent;
            if(existing!=null)return existing;
            var component=(CustomMapComponent)Activator.CreateInstance(type,c.Map);
            c.Map.components.Add(component);
            try{((ICampaignMapModule)component).Attach(c,spec);}
            catch{((ICampaignMapModule)component).Detach();c.Map.components.Remove(component);throw;}
            if(!c.MapSpec.Components.Any(s=>s.Type==spec.Type))c.MapSpec.Components.Add(Json.Copy(spec));
            return component;
        }
        public static void Remove(CampaignController c,string typeName) {
            var type=Resolve(typeName);var component=c.Map.components.FirstOrDefault(x=>x.GetType()==type);
            if(component==null)return;
            ((ICampaignMapModule)component).Detach();c.Map.components.Remove(component);c.MapSpec.Components.RemoveAll(s=>Resolve(s.Type)==type);
        }
        public static void Enable(CampaignController c,string typeName,bool enabled) {
            var type=Resolve(typeName);var component=c.Map.components.FirstOrDefault(x=>x.GetType()==type);
            if(component==null)throw new InvalidOperationException("Component is not attached: "+typeName);
            ((ICampaignMapModule)component).SetEnabled(enabled);
            foreach(var spec in c.MapSpec.Components.Where(s=>Resolve(s.Type)==type))spec.Enabled=enabled;
        }
    }
}
