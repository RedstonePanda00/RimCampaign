using System.Collections.Generic;
namespace RimCampaign.Core {
    public class MapRewardSpec : Named {
        public string StockpileMarker { get; set; } = "";
        public List<DeviceLoot> Contents { get; set; } = new List<DeviceLoot>();
        public string Recipient { get; set; } = "hero";
        public bool KeepForExtraction { get; set; }
    }
}
