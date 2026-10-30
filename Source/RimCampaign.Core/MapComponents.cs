using System.Collections.Generic;

namespace RimCampaign.Core
{
    public class MapComponentSpec : Named
    {
        public string Type { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public Dictionary<string,string> Settings { get; set; } = new Dictionary<string,string>();
    }
    public class CombatPatrolSpec : Named
    {
        public string Pawn { get; set; } = "";
        public string Route { get; set; } = "";
        public float Radius { get; set; } = 30;
    }
}
