namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// One matchup in a league's schedule - two teams and the period they meet.
    ///
    /// The teams are the provider's own ids, like everything else here. The
    /// caller matches them to real teams.
    /// </summary>
    public class ProviderMatchup
    {
        public string LeagueId { get; set; }

        /// <summary>
        /// The scoring period. A week in Yahoo, and in Fantrax a week or a day
        /// depending on the league.
        /// </summary>
        public int Period { get; set; }

        public string AwayTeamId { get; set; }

        public string HomeTeamId { get; set; }

        public bool IsPlayoff { get; set; }
    }
}
