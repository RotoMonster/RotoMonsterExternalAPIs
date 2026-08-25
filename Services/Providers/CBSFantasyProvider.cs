using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Providers;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    /// <summary>
    /// CBS as an IFantasyProvider.
    ///
    /// CBS has no fantasy API. The league list happens to come from a JSON
    /// endpoint meant for their video product, and everything else is the
    /// league's own web pages, parsed.
    ///
    /// userKey is the CBS pid, which is the whole of the authentication. It
    /// goes across as a cookie and nothing else is needed - no login, no
    /// tokens. IMPORTANT: it must be sent UNENCODED. The pid contains colons,
    /// slashes and equals signs, and a URL encoded copy taken from a browser's
    /// cookie header is rejected and the request redirects to the login page.
    ///
    /// Because the pid never expires on its own, NeedsReauthorization is only
    /// set when CBS bounces us to login, which in practice means a bad pid.
    /// </summary>
    public class CBSFantasyProvider : IFantasyProvider
    {
        private const string LeagueListUrl = "https://www.cbssports.com/api/watch/user?pid=";

        /// <summary>
        /// Redirects are followed by hand so a bounce to the login page can be
        /// told apart from a real page.
        /// </summary>
        private static readonly HttpClient Http = new HttpClient(
            new HttpClientHandler { AllowAutoRedirect = false });

        /// <summary>
        /// CBS's own word for the sport, used both to pick the right block out
        /// of the league list and to build league URLs.
        /// </summary>
        private readonly string _sport;

        public CBSFantasyProvider(string sport)
        {
            if (string.IsNullOrEmpty(sport)) throw new ArgumentNullException(nameof(sport));
            _sport = SportCode(sport);
        }

        public string ProviderName
        {
            get { return "CBS"; }
        }

        // -------------------------------------------------------------------
        // League list
        // -------------------------------------------------------------------

        public async Task<GetProviderLeaguesResult> GetLeaguesAsync(string userKey, string seasonKey)
        {
            if (string.IsNullOrEmpty(userKey))
                return BaseResult.Failure<GetProviderLeaguesResult>("A CBS PID is required.");

            // This endpoint takes the pid in the query string rather than as a
            // cookie, and needs no other authentication at all.
            var response = await Get(LeagueListUrl + userKey, null).ConfigureAwait(false);
            if (!response.Ok)
                return BaseResult.Failure<GetProviderLeaguesResult>(response.Error);

            JsonElement root;
            if (!TryParse(response.Body, out root))
                return BaseResult.Failure<GetProviderLeaguesResult>("CBS returned a response we could not read.");

            var result = new GetProviderLeaguesResult { Success = true };

            var user = Prop(Prop(root, "data"), "UserByPid");
            var spoes = Prop(user, "spoes");

            if (spoes.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var block in spoes.EnumerateArray())
            {
                if (!string.Equals(Str(block, "category"), _sport, StringComparison.OrdinalIgnoreCase))
                    continue;

                var teams = Prop(block, "teams");
                if (teams.ValueKind != JsonValueKind.Array) continue;

                foreach (var team in teams.EnumerateArray())
                {
                    var leagueId = LeagueIdFromUrl(Str(team, "url"));
                    if (string.IsNullOrEmpty(leagueId)) continue;

                    result.Leagues.Add(new ProviderLeague
                    {
                        LeagueId = leagueId,

                        // Reads backwards but is right: CBS puts the league
                        // name in title and the user's team name in
                        // description.
                        Title = Str(team, "title"),
                        MyTeamTitle = Str(team, "description"),

                        // CBS does not say which season a league belongs to,
                        // so every league the user has ever had comes back
                        // together. Left empty rather than guessed at.
                        SeasonKey = ""
                    });
                }
            }

            return result;
        }

        // -------------------------------------------------------------------
        // League data
        // -------------------------------------------------------------------

        public async Task<GetProviderLeagueDataResult> GetLeagueDataAsync(
            string userKey,
            string seasonKey,
            IList<string> leagueIds,
            ProviderLeagueDataParts parts)
        {
            var result = new GetProviderLeagueDataResult { Success = true };

            // Rosters and drafts are not read yet, so the caller is told what
            // it actually got rather than what it asked for.
            result.PartsReturned = parts & ProviderLeagueDataParts.Settings;

            if (leagueIds == null || leagueIds.Count == 0)
                return result;

            foreach (var leagueId in leagueIds)
            {
                var entry = new ProviderLeagueData { LeagueId = leagueId };
                result.Leagues.Add(entry);

                if ((parts & ProviderLeagueDataParts.Settings) == 0)
                    continue;

                try
                {
                    var url = LeagueUrl(leagueId) + "/rules";
                    var response = await Get(url, userKey).ConfigureAwait(false);
                    result.RequestCount++;

                    if (!response.Ok)
                    {
                        entry.ErrorMessage = response.Error;
                        if (response.NeedsLogin) result.NeedsReauthorization = true;
                        continue;
                    }

                    entry.Settings = CBSRulesParser.Parse(leagueId, response.Body);

                    // An empty title means the page came back but was not the
                    // rules page, which is worth reporting rather than handing
                    // back settings full of defaults.
                    if (string.IsNullOrEmpty(entry.Settings.Title))
                        entry.ErrorMessage = "CBS did not return the rules page for this league.";
                }
                catch (Exception ex)
                {
                    entry.ErrorMessage = ex.Message;
                }
            }

            return result;
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private string LeagueUrl(string leagueId)
        {
            return "https://" + leagueId + "." + _sport + ".cbssports.com";
        }

        /// <summary>
        /// The league id is the subdomain. It usually resembles the league name
        /// but not always, so it is read off the url rather than derived from
        /// the title.
        /// </summary>
        private static string LeagueIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";

            var start = url.IndexOf("://", StringComparison.Ordinal);
            if (start >= 0) url = url.Substring(start + 3);

            var dot = url.IndexOf('.');
            return dot > 0 ? url.Substring(0, dot) : "";
        }

        /// <summary>
        /// RotoMonster's sport titles are three letter codes; CBS uses the full
        /// word in both its urls and its league list.
        /// </summary>
        private static string SportCode(string sport)
        {
            switch (sport.Trim().ToUpperInvariant())
            {
                case "NBA": return "basketball";
                case "MLB": return "baseball";
                case "NFL": return "football";
                case "NHL": return "hockey";
                default: return sport.Trim().ToLowerInvariant();
            }
        }

        private struct Response
        {
            public bool Ok;
            public string Body;
            public string Error;
            public bool NeedsLogin;
        }

        /// <summary>
        /// Sends the pid as a cookie when one is given. A 302 from a league
        /// page is CBS redirecting to login, which is the only way a bad pid
        /// shows itself - it does not return an error, it just bounces.
        /// </summary>
        private static async Task<Response> Get(string url, string pid)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrEmpty(pid))
                    {
                        // Deliberately not encoded. An encoded pid is rejected
                        // and the request lands on the login page instead.
                        request.Headers.Add("Cookie", "pid=" + pid);
                    }

                    using (var response = await Http.SendAsync(request).ConfigureAwait(false))
                    {
                        var status = (int)response.StatusCode;

                        if (status >= 300 && status < 400)
                        {
                            return new Response
                            {
                                Ok = false,
                                NeedsLogin = true,
                                Error = "CBS redirected to login. The saved PID is no longer valid."
                            };
                        }

                        if (!response.IsSuccessStatusCode)
                            return new Response { Ok = false, Error = "CBS returned " + status + "." };

                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (string.IsNullOrWhiteSpace(body))
                            return new Response { Ok = false, Error = "CBS returned an empty page." };

                        return new Response { Ok = true, Body = body };
                    }
                }
            }
            catch (Exception ex)
            {
                return new Response { Ok = false, Error = "Could not reach CBS. [" + ex.Message + "]" };
            }
        }

        private static bool TryParse(string content, out JsonElement root)
        {
            root = default(JsonElement);

            try
            {
                using (var doc = JsonDocument.Parse(content))
                {
                    root = doc.RootElement.Clone();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static JsonElement Prop(JsonElement node, string name)
        {
            JsonElement value;
            if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out value))
                return value;

            return default(JsonElement);
        }

        private static string Str(JsonElement node, string name)
        {
            var value = Prop(node, name);
            return value.ValueKind == JsonValueKind.String ? (value.GetString() ?? "") : "";
        }
    }
}
