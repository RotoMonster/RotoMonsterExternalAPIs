using RotoMonsterExternalAPIs.Client.Models;
using System.Threading.Tasks;

namespace RotoMonsterExternalAPIs.Client.Services.Yahoo
{
    /// <summary>
    /// Where a user's Yahoo tokens live. Implemented by each site against its
    /// own database, so this library never needs to know about either one.
    ///
    /// userKey is whatever identifies the user on the calling side.
    /// </summary>
    public interface IYahooTokenStore
    {
        /// <summary>Returns null when the user has not connected Yahoo.</summary>
        Task<YahooTokens> LoadAsync(string userKey);

        Task SaveAsync(string userKey, YahooTokens tokens);
    }
}
