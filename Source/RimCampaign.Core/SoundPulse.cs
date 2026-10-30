using System.Collections.Generic;

namespace RimCampaign.Core
{
    public class SoundPulseSpec
    {
        public DefRef Sound { get; set; } = new DefRef{Kind="SoundDef"};
        public float Radius { get; set; } = 12;
        public bool RespectWalls { get; set; } = true;
        public string Receivers { get; set; } = "Pawns";
        public List<string> Targets { get; set; } = new List<string>();
        public string Group { get; set; } = "";
        public string Faction { get; set; } = "";
        public bool GuardReaction { get; set; } = true;
        public bool Dangerous { get; set; }
    }
}
