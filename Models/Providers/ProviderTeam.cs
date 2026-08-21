using System.Collections.Generic;

namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// A team in a league, with its roster when rosters were requested.
    /// </summary>
    public class ProviderTeam
    {
        public string LeagueId { get; set; }

        public string TeamId { get; set; }

        public string Title { get; set; }

        /// <summary>
        /// Where the provider gives a draft slot for the team.
        /// </summary>
        public int DraftOrder { get; set; }

        public bool IsMyTeam { get; set; }

        /// <summary>
        /// Empty when rosters were not asked for, rather than null, so callers
        /// do not have to check both.
        /// </summary>
        public List<ProviderRosterPlayer> Players { get; set; } = new List<ProviderRosterPlayer>();
    }
}
