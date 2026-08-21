using System.Collections.Generic;
using RotoMonsterExternalAPIs.Client.Models.Providers;

namespace RotoMonsterExternalAPIs.Client.Models.Results
{
    public class GetProviderLeagueDataResult : BaseResult
    {
        /// <summary>
        /// One entry per league asked for, in the order requested. A league the
        /// provider failed on still appears, with its ErrorMessage set, so a
        /// bad league in a batch of twenty does not lose the other nineteen.
        /// </summary>
        public List<ProviderLeagueData> Leagues { get; set; } = new List<ProviderLeagueData>();

        /// <summary>
        /// What was actually fetched. Can be less than requested if a provider
        /// does not support a part.
        /// </summary>
        public ProviderLeagueDataParts PartsReturned { get; set; }

        public bool NeedsReauthorization { get; set; }

        /// <summary>
        /// How many requests this took. Only for logging, but worth having
        /// when the whole point of the batching is call count.
        /// </summary>
        public int RequestCount { get; set; }
    }
}
