using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Providers;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    public class NhlPositionFeeds
    {
        public const string FanTraxPlayersUrl = "https://www.fantrax.com/fxea/general/getPlayerIds?sport=NHL";
        public const string FanTraxLeagueUrl = "https://www.fantrax.com/fxea/general/getLeagueInfo?leagueId={0}";
        public const string YahooPlayersUrl = "https://pub-api-ro.fantasysports.yahoo.com/fantasy/v2/league/{0}.l.public;out=settings/players;position=ALL;start={1};count={2};sort=rank_season?format=json_f";
        public const string EspnPlayersUrl = "https://lm-api-reads.fantasy.espn.com/apis/v3/games/fhl/seasons/{0}/players?scoringPeriodId=0&view=players_wl";
        public const string EspnFilter = "{\"players\":{\"limit\":10000,\"sortPercOwned\":{\"sortAsc\":false,\"sortPriority\":1}}}";

        private const int YahooPageSize = 2000;

        private static readonly HashSet<string> RealPositions =
            new HashSet<string>(new[] { "C", "LW", "RW", "D", "G" }, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<int, string> EspnDefaultPositions = new Dictionary<int, string>
        {
            { 1, "C" }, { 2, "LW" }, { 3, "RW" }, { 4, "D" }, { 5, "G" }
        };

        private static readonly Dictionary<int, string> EspnSlots = new Dictionary<int, string>
        {
            { 0, "C" }, { 1, "LW" }, { 2, "RW" }, { 4, "D" }, { 5, "G" }
        };

        private readonly HttpClient _http;

        public NhlPositionFeeds(HttpClient http = null)
        {
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        }

        public async Task<ProviderPositionsResult> GetFanTraxPlayersAsync()
        {
            var text = await GetAsync(FanTraxPlayersUrl, null).ConfigureAwait(false);
            if (text.Error != null) return BaseResult.Failure<ProviderPositionsResult>(text.Error);

            var result = new ProviderPositionsResult { Success = true };
            try
            {
                using (var doc = JsonDocument.Parse(text.Body))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var v = prop.Value;
                        var position = Str(v, "position");
                        if (string.IsNullOrWhiteSpace(position) || position.StartsWith("Tm", StringComparison.OrdinalIgnoreCase)) continue;

                        var player = new ProviderPlayerPosition
                        {
                            ProviderPlayerId = Str(v, "fantraxId") ?? prop.Name,
                            SportRadarId = Str(v, "sportRadarId"),
                            StatsIncId = Str(v, "statsIncId"),
                            Name = FlipName(Str(v, "name")),
                            Team = Str(v, "team")
                        };
                        AddPositions(player, position);
                        result.Players.Add(player);
                    }
                }
            }
            catch (JsonException ex)
            {
                return BaseResult.Failure<ProviderPositionsResult>("FanTrax players: " + ex.Message);
            }

            return result;
        }

        public async Task<ProviderPositionsResult> GetFanTraxLeaguePositionsAsync(string leagueId)
        {
            if (string.IsNullOrWhiteSpace(leagueId)) return BaseResult.Failure<ProviderPositionsResult>("No FanTrax league id.");

            var text = await GetAsync(string.Format(FanTraxLeagueUrl, Uri.EscapeDataString(leagueId)), null).ConfigureAwait(false);
            if (text.Error != null) return BaseResult.Failure<ProviderPositionsResult>(text.Error);

            var result = new ProviderPositionsResult { Success = true };
            try
            {
                using (var doc = JsonDocument.Parse(text.Body))
                {
                    JsonElement info;
                    if (!doc.RootElement.TryGetProperty("playerInfo", out info) || info.ValueKind != JsonValueKind.Object)
                        return BaseResult.Failure<ProviderPositionsResult>("FanTrax league has no playerInfo.");

                    foreach (var prop in info.EnumerateObject())
                    {
                        var player = new ProviderPlayerPosition { ProviderPlayerId = prop.Name };
                        AddPositions(player, Str(prop.Value, "eligiblePos"));
                        if (player.Positions.Count > 0) result.Players.Add(player);
                    }
                }
            }
            catch (JsonException ex)
            {
                return BaseResult.Failure<ProviderPositionsResult>("FanTrax league: " + ex.Message);
            }

            return result;
        }

        public async Task<ProviderPositionsResult> GetYahooPlayersAsync(string gameKey)
        {
            if (string.IsNullOrWhiteSpace(gameKey)) return BaseResult.Failure<ProviderPositionsResult>("No Yahoo game key.");

            var result = new ProviderPositionsResult { Success = true };
            var start = 0;

            while (true)
            {
                var text = await GetAsync(string.Format(YahooPlayersUrl, gameKey, start, YahooPageSize), null).ConfigureAwait(false);
                if (text.Error != null) return BaseResult.Failure<ProviderPositionsResult>(text.Error);

                var count = 0;
                try
                {
                    using (var doc = JsonDocument.Parse(text.Body))
                    {
                        JsonElement content, league, players;
                        if (!doc.RootElement.TryGetProperty("fantasy_content", out content)
                            || !content.TryGetProperty("league", out league)
                            || !league.TryGetProperty("players", out players)
                            || players.ValueKind != JsonValueKind.Array)
                            break;

                        foreach (var item in players.EnumerateArray())
                        {
                            JsonElement p;
                            if (!item.TryGetProperty("player", out p)) continue;
                            count++;

                            var player = new ProviderPlayerPosition
                            {
                                ProviderPlayerId = Str(p, "player_id"),
                                Team = Str(p, "editorial_team_abbr")
                            };

                            JsonElement name;
                            if (p.TryGetProperty("name", out name)) player.Name = Str(name, "full");

                            AddPositions(player, Str(p, "display_position"));
                            if (!string.IsNullOrEmpty(player.ProviderPlayerId) && player.Positions.Count > 0) result.Players.Add(player);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    return BaseResult.Failure<ProviderPositionsResult>("Yahoo players: " + ex.Message);
                }

                if (count < YahooPageSize) break;
                start += count;
            }

            return result;
        }

        public async Task<ProviderPositionsResult> GetEspnPlayersAsync(int espnSeason)
        {
            var text = await GetAsync(string.Format(EspnPlayersUrl, espnSeason), EspnFilter).ConfigureAwait(false);
            if (text.Error != null) return BaseResult.Failure<ProviderPositionsResult>(text.Error);

            var result = new ProviderPositionsResult { Success = true };
            try
            {
                using (var doc = JsonDocument.Parse(text.Body))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                        return BaseResult.Failure<ProviderPositionsResult>("ESPN returned no players.");

                    foreach (var p in doc.RootElement.EnumerateArray())
                    {
                        var player = new ProviderPlayerPosition
                        {
                            ProviderPlayerId = Str(p, "id"),
                            Name = Str(p, "fullName")
                        };

                        int defaultId;
                        string code;
                        if (int.TryParse(Str(p, "defaultPositionId"), out defaultId) && EspnDefaultPositions.TryGetValue(defaultId, out code))
                            player.Positions.Add(code);

                        JsonElement slots;
                        if (p.TryGetProperty("eligibleSlots", out slots) && slots.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var s in slots.EnumerateArray())
                            {
                                int slot;
                                if (s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out slot) && EspnSlots.TryGetValue(slot, out code)
                                    && !player.Positions.Contains(code))
                                    player.Positions.Add(code);
                            }
                        }

                        if (!string.IsNullOrEmpty(player.ProviderPlayerId) && player.Positions.Count > 0) result.Players.Add(player);
                    }
                }
            }
            catch (JsonException ex)
            {
                return BaseResult.Failure<ProviderPositionsResult>("ESPN players: " + ex.Message);
            }

            return result;
        }

        private static void AddPositions(ProviderPlayerPosition player, string codes)
        {
            if (string.IsNullOrWhiteSpace(codes)) return;
            foreach (var raw in codes.Split(','))
            {
                var code = raw.Trim().ToUpperInvariant();
                if (RealPositions.Contains(code) && !player.Positions.Contains(code)) player.Positions.Add(code);
            }
        }

        private async Task<HttpText> GetAsync(string url, string espnFilter)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; RotoMonster)");
                    request.Headers.TryAddWithoutValidation("Accept", "application/json, */*");
                    if (espnFilter != null) request.Headers.TryAddWithoutValidation("X-Fantasy-Filter", espnFilter);

                    using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                            return new HttpText { Error = (int)response.StatusCode + " from " + new Uri(url).Host };
                        return new HttpText { Body = body };
                    }
                }
            }
            catch (Exception ex)
            {
                return new HttpText { Error = new Uri(url).Host + ": " + ex.Message };
            }
        }

        private static string Str(JsonElement element, string property)
        {
            JsonElement value;
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out value)) return null;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind == JsonValueKind.Number) return value.GetRawText();
            return null;
        }

        private static string FlipName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            var comma = name.IndexOf(',');
            if (comma < 0) return name.Trim();
            return (name.Substring(comma + 1).Trim() + " " + name.Substring(0, comma).Trim()).Trim();
        }

        private class HttpText
        {
            public string Body;
            public string Error;
        }
    }
}
