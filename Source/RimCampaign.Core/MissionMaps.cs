using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RimCampaign.Core {
    public sealed class MissionMapSpec : Named {
        public string Format {get;set;}="RimCampaign.MissionMap/1";
        public Project Project {get;set;}
    }
    public static class MissionMaps {
        public static void Validate(MissionMapSpec mission){
            var p=mission?.Project;
            if(mission?.Format!="RimCampaign.MissionMap/1"||p==null||p.Maps.Count!=1||p.Campaigns.Count!=1||p.Campaigns[0].Nodes.Count!=1)throw new InvalidDataException("A mission map must contain exactly one map and one mission node.");
            if(p.Maps[0].Base!="PocketMission"||p.Maps[0].TerrainInitialization!=null)throw new InvalidDataException("A pocket mission takes its ecology from its destination world tile.");
            var errors=Validation.Check(p);if(errors.Count>0)throw new InvalidDataException(string.Join("\n",errors));
        }
        public static string DefName(MissionMapSpec m)=>"RC_Mission_"+m.Id.Replace("-","_");
        public static void Export(string file,MissionMapSpec mission){
            Validate(mission);Json.Save(file,mission);
            var id=DefName(mission);var map=mission.Project.Maps[0];
            new XDocument(new XElement("Defs",
                new XElement("RimCampaign.Runtime.MissionMapDef",new XElement("defName",id),new XElement("label",mission.Name),new XElement("missionFile","MissionMaps/"+Path.GetFileName(file)),new XElement("sitePart",id+"_Part")),
                new XElement("SitePartDef",new XAttribute("ParentName","RC_MissionMapPartBase"),new XElement("defName",id+"_Part"),new XElement("label",mission.Name),new XElement("minMapSize","("+map.Width+", 1, "+map.Height+")")))).Save(Path.ChangeExtension(file,"xml"));
        }
    }
}
