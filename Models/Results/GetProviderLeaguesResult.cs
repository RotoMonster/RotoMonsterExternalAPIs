using System.Collections.Generic;
using RotoMonsterExternalAPIs.Client.Models.Providers;

namespace RotoMonsterExternalAPIs.Client.Models.Results
{
    public class GetProviderLeaguesResult : BaseResult
    {
        public List<ProviderLeague> Leagues { get; set; } = new List<ProviderLeague>();

        /// <summary>
        /// True when the provider rejected the credentials rather than failing
        /// for some other reason. Tells the page to send the user back through
        /// connect instead of offering a retry.
        /// </summary>
        public bool NeedsReauthorization { get; set; }
    }
}
