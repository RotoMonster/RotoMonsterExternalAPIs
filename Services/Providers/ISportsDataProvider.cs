using System;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Providers;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    /// <summary>
    /// One source of schedule, roster and box score data, in the same shape
    /// whatever is underneath. MySportsFeeds is the default; nflverse sits
    /// behind the same interface for the columns MySportsFeeds does not carry.
    ///
    /// Sport is a parameter rather than an interface per sport. The providers
    /// use the same urls, auth and feed names for all four leagues and only
    /// the stat field names differ, so one implementation covers them and
    /// adding a sport is a mapping rather than a new provider.
    ///
    /// Nothing here returns RM ids, same as IFantasyProvider. Players, teams
    /// and games come back with the provider's own ids and codes, and matching
    /// them to real records needs the database, so that stays with the caller.
    ///
    /// Every call takes previousLastUpdated, which is the LastUpdatedOn the
    /// last call returned, or null to fetch regardless. When the feed has not
    /// changed since then the result comes back NotModified with nothing in
    /// it, which is not a failure and means there is nothing to write.
    ///
    /// This is what makes polling every minute during games reasonable. Note
    /// it does not save the request itself, only the work of reprocessing the
    /// same data. MySportsFeeds documents a 304 for this but does not actually
    /// send one, so their own change timestamp is used instead.
    /// </summary>
    public interface ISportsDataProvider
    {
        /// <summary>Matches the Name column on FantasyProviders.</summary>
        string ProviderName { get; }

        /// <summary>True if this provider can serve the sport at all.</summary>
        bool Supports(SportsDataSport sport);

        /// <summary>
        /// Every game in the season, with scores on the ones that have
        /// finished. Season is the provider's own key, e.g. 2026-regular.
        /// </summary>
        Task<GetSportsDataGamesResult> GetGamesAsync(
            SportsDataSport sport, string season, string previousLastUpdated);

        Task<GetSportsDataTeamsResult> GetTeamsAsync(
            SportsDataSport sport, string season, string previousLastUpdated);

        /// <summary>
        /// Every player, with the biographical fields needed to create one of
        /// ours, and their current injury where they have one.
        /// </summary>
        Task<GetSportsDataPlayersResult> GetPlayersAsync(
            SportsDataSport sport, string previousLastUpdated);

        /// <summary>
        /// Box score lines for a week.
        ///
        /// Takes a week rather than a game because their feed returns every
        /// player for a whole week in one request, and an interface that only
        /// accepted one game would make that impossible to use. A provider
        /// without that would loop internally.
        /// </summary>
        Task<GetSportsDataPlayerGamesResult> GetPlayerGamesAsync(
            SportsDataSport sport, string season, int week, string previousLastUpdated);

        Task<GetSportsDataPlayerGamesResult> GetPlayerGamesByDateAsync(
            SportsDataSport sport, string season, DateTime date, string previousLastUpdated);
    }
}
