using System.Collections.Generic;

namespace RimCampaign.Core {
    // Opt-in presentation data, independent of map logic modules and mission rewards.
    public sealed class OpeningTitleSpec {
        public bool ManualStart { get; set; }
        public string Title { get; set; } = "";
        public List<string> Lines { get; set; } = new List<string>();
        public string TitleColor { get; set; } = "#FFFFFF";
        public string TextColor { get; set; } = "#FFFFFF";
        public float FadeIn { get; set; } = 1;
        public float Hold { get; set; } = 6;
        public float FadeOut { get; set; } = 1.5f;
    }
}
