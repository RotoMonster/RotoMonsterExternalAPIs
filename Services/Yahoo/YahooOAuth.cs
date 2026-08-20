using RotoMonsterExternalAPIs.Client.Models.Results;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RotoMonsterExternalAPIs.Client.Services.Yahoo
{
    /// <summary>
    /// Yahoo OAuth 2.0. Ported from the VB YahooOAuth class.
    ///
    /// The flow:
    ///   1. GetAuthorizationUrl  - send the user there to grant access
    ///   2. Yahoo shows them a code (oob) or calls back with one
    ///   3. ExchangeCodeAsync    - trade that code for tokens
    ///   4. GetAsync             - call the API
    ///   5. RefreshAsync         - access tokens last an hour
    ///
    /// This class holds no state and does no storage. It takes tokens and hands
    /// new ones back, so the caller decides where they live.
    /// </summary>
    public class YahooOAuth
    {
        public const string OutOfBandRedirect = "oob";

        private const string AuthorizeUrl = "https://api.login.yahoo.com/oauth2/request_auth";
        private const string TokenUrl = "https://api.login.yahoo.com/oauth2/get_token";

        /// <summary>
        /// Refreshing a minute early rather than on the stroke of expiry, so a
        /// token cannot lapse between the check and the call landing at Yahoo.
        /// </summary>
        private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

        // One instance for the process. A new HttpClient per call leaks sockets
        // under load, because disposing it does not release the connection
        // immediately.
        private static readonly HttpClient Http = new HttpClient();

        private readonly string _consumerKey;
        private readonly string _consumerSecret;

        public YahooOAuth(string consumerKey, string consumerSecret)
        {
            if (string.IsNullOrWhiteSpace(consumerKey))
                throw new ArgumentException("Consumer key cannot be empty", nameof(consumerKey));
            if (string.IsNullOrWhiteSpace(consumerSecret))
                throw new ArgumentException("Consumer secret cannot be empty", nameof(consumerSecret));

            _consumerKey = consumerKey;
            _consumerSecret = consumerSecret;
        }

        /// <summary>
        /// The URL to send the user to so they can grant access.
        ///
        /// The redirect matters more than it looks. Yahoo requires the SAME
        /// redirect_uri here and on the token exchange, so whatever is passed in
        /// has to be passed to ExchangeCodeAsync as well. The VB version took a
        /// callback URL here but hardcoded oob on the exchange, which meant a
        /// real callback could never have worked.
        ///
        /// Leave redirectUri null for the out-of-band flow, where Yahoo displays
        /// a code for the user to copy. Pass a registered callback URL to have
        /// Yahoo redirect there with ?code=... instead.
        /// </summary>
        public string GetAuthorizationUrl(string redirectUri = null, string state = null)
        {
            var target = string.IsNullOrWhiteSpace(redirectUri) ? OutOfBandRedirect : redirectUri;

            var url = AuthorizeUrl +
                "?client_id=" + Uri.EscapeDataString(_consumerKey) +
                "&response_type=code" +
                "&language=en-us" +
                "&redirect_uri=" + Uri.EscapeDataString(target);

            // Only meaningful for the callback flow, where it comes back on the
            // query string and proves the response answers our request.
            if (!string.IsNullOrWhiteSpace(state))
                url += "&state=" + Uri.EscapeDataString(state);

            return url;
        }

        /// <summary>
        /// Trades the code from the authorization step for an access token and a
        /// refresh token. redirectUri must match what GetAuthorizationUrl used.
        /// </summary>
        public Task<YahooTokenResult> ExchangeCodeAsync(string code, string redirectUri = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                return Task.FromResult(BaseResult.Failure<YahooTokenResult>("Authorization code is required."));

            var form = new Dictionary<string, string>
            {
                { "client_id", _consumerKey },
                { "client_secret", _consumerSecret },
                { "redirect_uri", string.IsNullOrWhiteSpace(redirectUri) ? OutOfBandRedirect : redirectUri },
                { "code", code },
                { "grant_type", "authorization_code" }
            };

            return PostForTokensAsync(form);
        }

        /// <summary>
        /// Exchanges a refresh token for a fresh pair. Yahoo access tokens last
        /// an hour; the refresh token that comes back replaces the one used.
        /// </summary>
        public Task<YahooTokenResult> RefreshAsync(string refreshToken, string redirectUri = null)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Task.FromResult(BaseResult.Failure<YahooTokenResult>("Refresh token is required."));

            var form = new Dictionary<string, string>
            {
                { "client_id", _consumerKey },
                { "client_secret", _consumerSecret },
                { "redirect_uri", string.IsNullOrWhiteSpace(redirectUri) ? OutOfBandRedirect : redirectUri },
                { "refresh_token", refreshToken },
                { "grant_type", "refresh_token" }
            };

            return PostForTokensAsync(form);
        }

        /// <summary>
        /// Calls a Yahoo API url with a bearer token. Append format=json to the
        /// url for JSON; Yahoo returns XML otherwise.
        /// </summary>
        public async Task<YahooApiResult> GetAsync(string url, string accessToken)
        {
            if (string.IsNullOrWhiteSpace(url))
                return BaseResult.Failure<YahooApiResult>("Url is required.");
            if (string.IsNullOrWhiteSpace(accessToken))
                return BaseResult.Failure<YahooApiResult>("Access token is required.");

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);

                    using (var response = await Http.SendAsync(request).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (response.IsSuccessStatusCode)
                        {
                            return new YahooApiResult
                            {
                                Success = true,
                                Content = body,
                                StatusCode = response.StatusCode
                            };
                        }

                        var result = BaseResult.Failure<YahooApiResult>(
                            "Yahoo returned " + (int)response.StatusCode + ": " + Truncate(body, 500));

                        result.StatusCode = response.StatusCode;

                        // An expired access token is not a failed call - refresh
                        // and try again. Worth telling apart from a real error.
                        result.TokenRejected = response.StatusCode == HttpStatusCode.Unauthorized;

                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<YahooApiResult>(ex.Message);
            }
        }

        /// <summary>
        /// True when a token stored with this expiry should be refreshed before
        /// being used.
        /// </summary>
        public static bool NeedsRefresh(DateTime expiresAtUtc)
        {
            return DateTime.UtcNow >= expiresAtUtc - ExpiryBuffer;
        }

        private async Task<YahooTokenResult> PostForTokensAsync(Dictionary<string, string> form)
        {
            try
            {
                using (var content = new FormUrlEncodedContent(form))
                using (var response = await Http.PostAsync(TokenUrl, content).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var failure = BaseResult.Failure<YahooTokenResult>(
                            "Yahoo returned " + (int)response.StatusCode + ": " + Truncate(body, 500));

                        // invalid_grant means the code or refresh token is dead -
                        // revoked, already used, or expired. Retrying cannot help;
                        // the user has to authorize again.
                        failure.NeedsReauthorization =
                            body != null && body.IndexOf("invalid_grant", StringComparison.OrdinalIgnoreCase) >= 0;

                        return failure;
                    }

                    return Parse(body);
                }
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<YahooTokenResult>(ex.Message);
            }
        }

        private static YahooTokenResult Parse(string body)
        {
            using (var document = JsonDocument.Parse(body))
            {
                var root = document.RootElement;

                var accessToken = ReadString(root, "access_token");
                var refreshToken = ReadString(root, "refresh_token");

                if (string.IsNullOrEmpty(accessToken))
                    return BaseResult.Failure<YahooTokenResult>("Yahoo response contained no access token.");

                // expires_in is seconds from now. Falling back to an hour only if
                // Yahoo ever omits it, which it currently does not.
                var seconds = 3600;
                if (root.TryGetProperty("expires_in", out var expiresIn))
                {
                    if (expiresIn.ValueKind == JsonValueKind.Number)
                        seconds = expiresIn.GetInt32();
                    else if (expiresIn.ValueKind == JsonValueKind.String &&
                             int.TryParse(expiresIn.GetString(), out var parsed))
                        seconds = parsed;
                }

                return new YahooTokenResult
                {
                    Success = true,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    ExpiresAtUtc = DateTime.UtcNow.AddSeconds(seconds),
                    YahooGuid = ReadString(root, "xoauth_yahoo_guid")
                };
            }
        }

        private static string ReadString(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }
    }
}
