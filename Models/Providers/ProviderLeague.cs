namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// One league as it appears in a provider's league list. Just enough to
    /// show a row and let the user decide whether to import it - the settings,
    /// rosters and draft come later and only for the ones they picked.
    /// </summary>
    public class ProviderLeague
    {
        /// <summary>
        /// The provider's own id for the league. Passed back in to fetch
        /// anything else about it, and stored on UserLeague.ProviderLeagueId.
        /// Yahoo uses a compound key (game.l.id), Fantrax a plain string, so
        /// this stays opaque rather than being parsed.
        /// </summary>
        public string LeagueId { get; set; }

        public string Title { get; set; }

        /// <summary>
        /// The user's own team in this league, where the provider tells us.
        /// Yahoo returns it with the league list, Fantrax does too. ESPN needs
        /// a separate lookup, so these can be empty.
        /// </summary>
        public string MyTeamId { get; set; }

        public string MyTeamTitle { get; set; }

        /// <summary>
        /// The provider's sport/season identifier this league sits under.
        /// Kept so a caller listing several seasons can tell them apart.
        /// </summary>
        public string SeasonKey { get; set; }
    }
}
