using System.Collections.Generic;

namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// Everything fetched about one league, holding only the parts that were
    /// asked for. A part that was not requested is left null so the caller can
    /// tell "not fetched" from "fetched and empty" - an important difference
    /// for a league with no draft yet.
    /// </summary>
    public class ProviderLeagueData
    {
        public string LeagueId { get; set; }

        /// <summary>
        /// Null unless Settings was requested.
        /// </summary>
        public ProviderLeagueSettings Settings { get; set; }

        /// <summary>
        /// Null unless Rosters was requested.
        /// </summary>
        public List<ProviderTeam> Teams { get; set; }

        /// <summary>
        /// Null unless Drafts was requested. Empty when the league has not
        /// drafted yet.
        /// </summary>
        public List<ProviderDraftPick> DraftPicks { get; set; }

        /// <summary>
        /// Null unless Waivers was requested. Empty when nobody is on waivers,
        /// and also empty where the provider was asked but the league has
        /// continuous waivers - see the note on that in the Yahoo provider.
        /// </summary>
        public List<ProviderRosterPlayer> WaiverPlayers { get; set; }

        /// <summary>
        /// Anything the provider refused or returned badly for this league
        /// alone. One league failing inside a batch should not fail the rest,
        /// so the failure is recorded here and the caller decides.
        /// </summary>
        public string ErrorMessage { get; set; }

        public bool HasError
        {
            get { return !string.IsNullOrEmpty(ErrorMessage); }
        }
    }
}
