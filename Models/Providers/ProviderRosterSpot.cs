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
    }
}
