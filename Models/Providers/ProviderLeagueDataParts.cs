using System;

namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// What the caller wants back about a league.
    ///
    /// Flags rather than separate methods because providers bundle this data
    /// differently. Yahoo returns settings and draft results together in one
    /// request, so asking for them separately would throw that away. Fantrax
    /// has an endpoint per part and fetches them one at a time. Either way
    /// the caller says what it needs and the provider makes the fewest calls
    /// it can.
    /// </summary>
    [Flags]
    public enum ProviderLeagueDataParts
    {
        None = 0,
        Settings = 1,
        Rosters = 2,
        Drafts = 4,

        /// <summary>
        /// The players on the waiver wire. Kept out of All on purpose - it is
        /// the most expensive part to fetch, since providers page it, and a
        /// caller refreshing rosters rarely wants the whole wire as well.
        /// </summary>
        Waivers = 8,

        /// <summary>
        /// Who plays who, by period. Out of All alongside Waivers - a schedule
        /// does not change once it is set, so refetching it with every roster
        /// refresh is wasted.
        /// </summary>
        Schedule = 16,

        All = Settings | Rosters | Drafts
    }
}
