using RotoMonsterExternalAPIs.Client.Models;
using RotoMonsterExternalAPIs.Client.Models.Results;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RotoMonsterExternalAPIs.Client.Services.Yahoo
{
    /// <summary>
    /// Yahoo API calls with token handling done for you. Callers say "get this
    /// url for this user" and refreshing happens underneath.
    ///
    /// Use YahooOAuth directly if you want to manage tokens yourself.
    /// </summary>
    public class YahooApiClient
    {
        private readonly YahooOAuth _oauth;
        private readonly IYahooTokenStore _store;
        private readonly string _redirectUri;

        // One lock per user. Two calls refreshing the same user at once would
        // each get tokens, and whichever saved second would win - leaving the
        // other holding a token that is no longer stored.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks =
            new ConcurrentDictionary<string, SemaphoreSlim>();

        public YahooApiClient(YahooOAuth oauth, IYahooTokenStore store, string redirectUri = null)
        {
            _oauth = oauth ?? throw new ArgumentNullException(nameof(oauth));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _redirectUri = redirectUri;
        }

        /// <summary>
        /// Completes the authorization flow: trades the code for tokens and
        /// stores them against this user.
        /// </summary>
        public async Task<YahooTokenResult> ConnectAsync(string userKey, string code)
        {
            var result = await _oauth.ExchangeCodeAsync(code, _redirectUri).ConfigureAwait(false);
            if (!result.Success) return result;

            await SaveAsync(userKey, result).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Calls a Yahoo API url as this user, refreshing first if the stored
        /// token has expired.
        /// </summary>
        public async Task<YahooApiResult> GetAsync(string userKey, string url)
        {
            var tokens = await _store.LoadAsync(userKey).ConfigureAwait(false);

            if (tokens == null || string.IsNullOrEmpty(tokens.RefreshToken))
            {
                var notConnected = BaseResult.Failure<YahooApiResult>(
                    "This user has not connected their Yahoo account.");
                notConnected.TokenRejected = true;
                return notConnected;
            }

            if (YahooOAuth.NeedsRefresh(tokens.ExpiresAtUtc) || string.IsNullOrEmpty(tokens.AccessToken))
            {
                var refreshed = await RefreshAsync(userKey, tokens).ConfigureAwait(false);
                if (!refreshed.Success)
                    return Rejected(refreshed);

                tokens = ToTokens(refreshed);
            }

            var result = await _oauth.GetAsync(url, tokens.AccessToken).ConfigureAwait(false);

            // Yahoo can reject a token we believed was still good - a clock skew,
            // or the user revoking access. One refresh and one retry, then give
            // up rather than looping.
            if (result.TokenRejected)
            {
                var refreshed = await RefreshAsync(userKey, tokens).ConfigureAwait(false);
                if (!refreshed.Success)
                    return Rejected(refreshed);

                result = await _oauth.GetAsync(url, refreshed.AccessToken).ConfigureAwait(false);
            }

            return result;
        }

        private async Task<YahooTokenResult> RefreshAsync(string userKey, YahooTokens current)
        {
            var gate = Locks.GetOrAdd(userKey ?? string.Empty, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);

            try
            {
                // Another call may have refreshed while we waited on the lock.
                var latest = await _store.LoadAsync(userKey).ConfigureAwait(false);
                if (latest != null &&
                    !string.IsNullOrEmpty(latest.AccessToken) &&
                    latest.AccessToken != current.AccessToken &&
                    !YahooOAuth.NeedsRefresh(latest.ExpiresAtUtc))
                {
                    return new YahooTokenResult
                    {
                        Success = true,
                        AccessToken = latest.AccessToken,
                        RefreshToken = latest.RefreshToken,
                        ExpiresAtUtc = latest.ExpiresAtUtc
                    };
                }

                var refreshToken = latest?.RefreshToken ?? current.RefreshToken;

                var result = await _oauth.RefreshAsync(refreshToken, _redirectUri).ConfigureAwait(false);
                if (result.Success)
                    await SaveAsync(userKey, result).ConfigureAwait(false);

                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        private Task SaveAsync(string userKey, YahooTokenResult result)
        {
            return _store.SaveAsync(userKey, ToTokens(result));
        }

        private static YahooTokens ToTokens(YahooTokenResult result)
        {
            return new YahooTokens
            {
                AccessToken = result.AccessToken,
                RefreshToken = result.RefreshToken,
                ExpiresAtUtc = result.ExpiresAtUtc
            };
        }

        private static YahooApiResult Rejected(YahooTokenResult refreshed)
        {
            var failure = BaseResult.Failure<YahooApiResult>(refreshed.ErrorMessage);

            // Carries through whether the user needs to authorize again, or
            // whether this was just a bad moment worth retrying.
            failure.TokenRejected = refreshed.NeedsReauthorization;
            return failure;
        }
    }
}
