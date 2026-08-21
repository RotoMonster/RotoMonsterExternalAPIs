using System.Collections.Generic;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Providers;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    /// <summary>
    /// One fantasy provider, in the same shape whatever it is underneath.
    ///
    /// Data only. Connecting an account is deliberately NOT here, because the
    /// three providers do it in genuinely different ways - Yahoo is OAuth with
    /// refresh tokens, ESPN is cookies the user supplies, Fantrax is a Secret
    /// ID off their profile page. A shared Connect() would hide a difference
    /// that actually matters. Each provider keeps its own connect flow, and
    /// userKey below is however the caller looks that up afterwards.
    ///
    /// Nothing here returns RM ids. Players, teams and leagues come back with
    /// the provider's own ids, and matching them to real records needs the
    /// database, so that stays with the caller.
    /// </summary>
    public interface IFantasyProvider
    {
        /// <summary>
        /// Which provider this is, matching FantasyProviders.Name.
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Every league the user has for the given season. One request for most
        /// providers, which is why bulk import is worth doing at all.
        ///
        /// seasonKey is the provider's own season identifier, e.g. Yahoo's game
        /// key. Providers that do not need one ignore it.
        /// </summary>
        Task<GetProviderLeaguesResult> GetLeaguesAsync(string userKey, string seasonKey);

        /// <summary>
        /// Settings, rosters and draft results for the given leagues, fetching
        /// only the parts asked for.
        ///
        /// Takes a list rather than one league on purpose. Yahoo can return
        /// twenty leagues in a single request, and an interface that only
        /// accepted one would make that impossible to use. A provider without
        /// batching loops internally, and the caller does not have to know
        /// which kind it is talking to.
        ///
        /// Implementations should chunk large lists themselves rather than
        /// pushing that onto the caller.
        /// </summary>
        Task<GetProviderLeagueDataResult> GetLeagueDataAsync(
            string userKey,
            string seasonKey,
            IList<string> leagueIds,
            ProviderLeagueDataParts parts);
    }
}
