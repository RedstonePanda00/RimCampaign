using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RimCampaign.Core {
    // A reusable layout, independent of any mission's actors, triggers or progress.
    public sealed class StructureSpec : Named {
        public string Format { get; set; } = "RimCampaign.Structure/1";
        public int Width { get; set; }
        public int Height { get; set; }
        public List<PatrolRoute> PatrolRoutes { get; set; } = new List<PatrolRoute>();
        public List<CombatPatrolSpec> Patrols { get; set; } = new List<CombatPatrolSpec>();
        public List<Placement> Objects { get; set; } = new List<Placement>();
        public List<MapFaction> Factions { get; set; } = MapFactions.Defaults();

    }
    public static class Structures {
        public static void Validate(StructureSpec s) {
            if(s==null||s.Format!="RimCampaign.Structure/1")throw new InvalidDataException("Not a RimCampaign structure.");

            if(s.Width<1||s.Height<1||s.Width>500||s.Height>500||s.Objects==null||s.Objects.Count==0)throw new InvalidDataException("Empty or invalid structure bounds.");
            if(s.Objects.Select(p=>p.Id).Distinct().Count()!=s.Objects.Count)throw new InvalidDataException("Duplicate structure object IDs.");
            foreach(var p in s.Objects) {
                if(!new[]{"Thing","PawnKind","Terrain","Roof"}.Contains(p.Kind))throw new InvalidDataException("Structure cannot contain mission-bound objects: "+p.Name+" ("+p.Kind+")");
                if(p.Position==null||p.Position.X<0||p.Position.Z<0||p.Position.X>=s.Width||p.Position.Z>=s.Height)throw new InvalidDataException("Structure object outside bounds: "+p.Name);
                if(p.Def==null||(string.IsNullOrEmpty(p.Def.Name)&&p.Kind!="Roof"))throw new InvalidDataException("Missing definition: "+p.Name);
                if(p.Faction!="Neutral"&&!s.Factions.Any(f=>f.Id==p.Faction))throw new InvalidDataException("Unknown structure faction: "+p.Faction);
            }
            foreach(var binding in s.Patrols??new List<CombatPatrolSpec>()) {
                if(!s.Objects.Any(p=>p.Id==binding.Pawn&&p.Kind=="PawnKind")||!s.PatrolRoutes.Any(r=>r.Id==binding.Route&&r.Points.Count>0))throw new InvalidDataException("Preset patrol references a missing pawn or route.");
            }
            foreach(var route in s.PatrolRoutes??new List<PatrolRoute>())foreach(var point in route.Points)
                if(point.Position.X<0||point.Position.Z<0||point.Position.X>=s.Width||point.Position.Z>=s.Height)throw new InvalidDataException("Preset patrol extends outside its bounds.");
        }
        public static IEnumerable<DefRef> References(StructureSpec s) => s.Objects.SelectMany(p=>new[]{p.Def,p.Stuff}.Concat((p.Contents??new List<DeviceLoot>()).SelectMany(l=>new[]{l.Def,l.Stuff}))).Where(d=>d!=null&&!string.IsNullOrEmpty(d.Name));
        public static string DefName(StructureSpec s)=>"RC_Structure_"+s.Id.Replace("-","_");
        public static XElement Definition(StructureSpec s,string relativePath)=>new XElement("RimCampaign.Runtime.CampaignStructureDef",new XElement("defName",DefName(s)),new XElement("label",s.Name),new XElement("structureFile",relativePath));
        public static void Export(string file,StructureSpec s) {
            Validate(s);Json.Save(file,s);
        }
    }
}
