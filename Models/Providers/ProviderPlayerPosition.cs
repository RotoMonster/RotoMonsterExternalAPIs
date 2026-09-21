using System.Collections.Generic;

namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    public class ProviderPlayerPosition
    {
        public string ProviderPlayerId { get; set; }
        public string Name { get; set; }
        public string Team { get; set; }
        public List<string> Positions { get; set; } = new List<string>();
        public string SportRadarId { get; set; }
        public string StatsIncId { get; set; }
    }
}
