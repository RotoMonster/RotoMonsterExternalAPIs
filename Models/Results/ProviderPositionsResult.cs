using System.Collections.Generic;
using RotoMonsterExternalAPIs.Client.Models.Providers;

namespace RotoMonsterExternalAPIs.Client.Models.Results
{
    public class ProviderPositionsResult : BaseResult
    {
        public List<ProviderPlayerPosition> Players { get; set; } = new List<ProviderPlayerPosition>();
    }
}
