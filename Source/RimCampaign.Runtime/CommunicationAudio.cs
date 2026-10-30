using Verse;
using Verse.Sound;

namespace RimCampaign.Runtime {
    public static class CommunicationAudio {
        public static void PlayStartCue()=>DefDatabase<SoundDef>.GetNamed("RC_CommunicationStart").PlayOneShotOnCamera();
    }
}
