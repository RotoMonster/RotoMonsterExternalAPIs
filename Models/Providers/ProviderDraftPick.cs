namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// One pick from a completed draft. As with rosters, PlayerId and TeamId
    /// are the provider's ids.
    /// </summary>
    public class ProviderDraftPick
    {
        public string LeagueId { get; set; }

        public string PlayerId { get; set; }

        public string PlayerName { get; set; }

        public string TeamId { get; set; }

        /// <summary>
        /// Overall pick number, first pick is 1.
        /// </summary>
        public int PickNumber { get; set; }

        public int Round { get; set; }

        /// <summary>
        /// Null in a snake draft. Set only for auctions.
        /// </summary>
        public int? Price { get; set; }
    }
}
