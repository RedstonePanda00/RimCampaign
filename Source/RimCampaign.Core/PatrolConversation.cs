using System.Collections.Generic;

namespace RimCampaign.Core
{
    // Participants are placement IDs; their current positions determine when they can talk.
    public class PatrolConversation : Named
    {
        public string First { get; set; } = "";
        public string Second { get; set; } = "";
        public float ParticipantDistance { get; set; } = 4;
        public bool HearThroughWalls { get; set; }
        public List<Act> Lines { get; set; } = new List<Act>();
    }
    public class ConversationMemory
    {
        public bool Active { get; set; }
        public bool HeardAll { get; set; } = true;
        public bool Complete { get; set; }
        public bool StartNotified { get; set; }
        public bool TryNotifyStart(){if(StartNotified)return false;StartNotified=true;return true;}
        public int Line { get; set; }
        public int Ticks { get; set; }
    }
}
