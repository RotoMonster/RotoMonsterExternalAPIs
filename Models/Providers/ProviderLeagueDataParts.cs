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
        All = Settings | Rosters | Drafts
    }
}
