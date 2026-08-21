namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// A player on a provider roster.
    ///
    /// PlayerId is the PROVIDER's id, not an RM player id. Matching it to a
    /// real player needs FantasyProviderPlayers, which lives in the database,
    /// so that stays on the caller's side. Name is carried alongside for the
    /// cases where the id does not match anything and someone has to work out
    /// who this was.
    /// </summary>
    public class ProviderRosterPlayer
    {
        public string PlayerId { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The roster slot the provider has them in, in provider codes.
        /// </summary>
        public string PositionCode { get; set; }

        public bool IsActive { get; set; }

        public bool IsIR { get; set; }
    }
}
