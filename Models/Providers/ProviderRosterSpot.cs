namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// One roster position and how many of it a team carries.
    ///
    /// Grouped rather than one entry per slot, because that is how providers
    /// report it and how the caller stores it - RM matches Code against
    /// ActiveRosterSpot.Title or YahooTitle and keeps the count.
    ///
    /// Bench and injury slots are included here as well as being counted into
    /// PlayersPerTeam and IRSpots on the settings, so a caller that wants the
    /// raw list still has it.
    /// </summary>
    public class ProviderRosterSpot
    {
        public string Code { get; set; }

        public int Count { get; set; }

        /// <summary>
        /// A bench slot. Set by the provider, since only it knows that Yahoo
        /// means BN and another provider means something else. Callers use it
        /// to skip the slot when building their own active roster list.
        /// </summary>
        public bool IsBench { get; set; }

        /// <summary>
        /// An injury slot - Yahoo's IL, IR or DL. Counted into IRSpots rather
        /// than into the active roster.
        /// </summary>
        public bool IsInjured { get; set; }
    }
}
