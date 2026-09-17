using System;

namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// One game as the provider reports it. Team codes are theirs, not ours,
    /// so the caller maps them.
    /// </summary>
    public class SportsDataGame
    {
        public string GameId { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public int Week { get; set; }
        public string HomeTeamCode { get; set; }
        public string AwayTeamCode { get; set; }
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }

        /// <summary>Their own word for it, e.g. COMPLETED or LIVE.</summary>
        public string Status { get; set; }

        public bool IsFinished { get; set; }
        public bool IsInProgress { get; set; }
    }
}
