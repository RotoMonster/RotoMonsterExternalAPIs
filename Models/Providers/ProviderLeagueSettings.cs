using System;
using System.Collections.Generic;

namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// A league's settings as the provider reports them. Field names follow
    /// UserLeague so the mapping on the RM side stays obvious, but nothing
    /// here is an RM id - a value the provider does not supply is simply
    /// left at its default for the caller to fill in.
    /// </summary>
    public class ProviderLeagueSettings
    {
        public string LeagueId { get; set; }

        public string Title { get; set; }

        /// <summary>
        /// Provider's own wording, e.g. "head" or "roto" from Yahoo. Left raw
        /// rather than mapped to an enum, since each provider words it
        /// differently and the caller already has that mapping.
        /// </summary>
        public string ScoringSystem { get; set; }

        public string LeagueType { get; set; }

        public string LineupFrequency { get; set; }

        public int NumberOfTeams { get; set; }

        public int PlayersPerTeam { get; set; }

        public int IRSpots { get; set; }

        /// <summary>
        /// Day the scoring week starts, as System.DayOfWeek cast to int, which
        /// is what UserLeague.StartWeekday holds.
        /// </summary>
        public int StartWeekday { get; set; }

        public int GameLimit { get; set; }

        public bool SameDayTransactions { get; set; }

        public bool IsAuction { get; set; }

        public bool IsMoney { get; set; }

        public bool IsProLeague { get; set; }

        public bool IsDynasty { get; set; }

        public bool HasDrafted { get; set; }

        public DateTime? DraftDate { get; set; }

        public string WaiverType { get; set; }

        public string WaiverRule { get; set; }

        public bool ContinuousWaivers { get; set; }

        public int EntryFee { get; set; }

        /// <summary>
        /// Scoring categories in the provider's own codes. Mapping these to
        /// RM categories needs the database, so it stays with the caller.
        /// </summary>
        public List<ProviderCategory> Categories { get; set; } = new List<ProviderCategory>();

        /// <summary>
        /// Roster positions and counts, including bench and injury slots.
        /// PlayersPerTeam and IRSpots above are derived from these by the
        /// provider, since knowing which codes mean bench or injured is
        /// provider-specific.
        /// </summary>
        public List<ProviderRosterSpot> RosterSpots { get; set; } = new List<ProviderRosterSpot>();

        /// <summary>
        /// Anything the provider could not express properly and the user
        /// should know about, in plain words. The caller is expected to
        /// show these, so they are written for a person rather than a log.
        /// </summary>
        public List<string> Notes { get; set; } = new List<string>();
    }
}
