using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Providers;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    public class NflPositionFeeds
    {
        public const string FanTraxPlayersUrl = "https://www.fantrax.com/fxea/general/getPlayerIds?sport=NFL";
        public const string FanTraxLeagueUrl = "https://www.fantrax.com/fxea/general/getLeagueInfo?leagueId={0}";
        public const string YahooPlayersUrl = "https://pub-api-ro.fantasysports.yahoo.com/fantasy/v2/league/{0}.l.public;out=settings/players;position=ALL;start={1};count={2};sort=rank_season?format=json_f";
        public const string EspnPlayersUrl = "https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{0}/segments/0/leagues/{1}?view=players_wl";
        public const string EspnFilter = "{\"players\":{\"limit\":5000,\"sortPercOwned\":{\"sortAsc\":false,\"sortPriority\":1}}}";
        public const string NflversePlayersUrl = "https://github.com/nflverse/nflverse-data/releases/download/players/players.csv";

        private const int YahooPageSize = 2000;

        private static readonly Dictionary<int, string> EspnPositions = new Dictionary<int, string>
        {
            { 1, "QB" },
            { 2, "RB" },
            { 3, "WR" },
            { 4, "TE" },
            { 5, "K" },
            { 16, "DEF" }
        };

        private readonly HttpClient _http;

        public NflPositionFeeds(HttpClient http = null)
        {
            _http = http ?? new HttpClient();
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
                        var player = new ProviderPlayerPosition
                        {
                            ProviderPlayerId = Str(v, "fantraxId") ?? prop.Name,
                            SportRadarId = Str(v, "sportRadarId"),
                            StatsIncId = Str(v, "statsIncId")
                        };

                        if (string.Equals(position, "Tm", StringComparison.OrdinalIgnoreCase))
                        {
                            player.Name = Str(v, "teamName");
                            player.Team = Str(v, "teamShortName");
                            player.Positions.Add("DEF");
                        }
                        else
                        {
                            player.Name = FlipName(Str(v, "name"));
                            player.Team = Str(v, "team");
                            if (!string.IsNullOrWhiteSpace(position)) player.Positions.Add(position.Trim());
                        }

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
                        var eligible = Str(prop.Value, "eligiblePos");
                        if (string.IsNullOrWhiteSpace(eligible)) continue;

                        var player = new ProviderPlayerPosition { ProviderPlayerId = prop.Name };
                        foreach (var pos in eligible.Split(','))
                        {
                            var p = pos.Trim();
                            if (p.Length == 0 || string.Equals(p, "RWT", StringComparison.OrdinalIgnoreCase)) continue;
                            if (!player.Positions.Contains(p)) player.Positions.Add(p);
                        }

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
                var url = string.Format(YahooPlayersUrl, gameKey, start, YahooPageSize);
                var text = await GetAsync(url, null).ConfigureAwait(false);
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

                            var display = Str(p, "display_position");
                            if (!string.IsNullOrWhiteSpace(display))
                            {
                                foreach (var pos in display.Split(','))
                                {
                                    var code = pos.Trim();
                                    if (code.Length > 0 && !player.Positions.Contains(code)) player.Positions.Add(code);
                                }
                            }

                            if (!string.IsNullOrEmpty(player.ProviderPlayerId)) result.Players.Add(player);
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

        public async Task<ProviderPositionsResult> GetEspnPlayersAsync(int season, string leagueId)
        {
            if (string.IsNullOrWhiteSpace(leagueId)) return BaseResult.Failure<ProviderPositionsResult>("No ESPN league id.");

            var url = string.Format(EspnPlayersUrl, season, Uri.EscapeDataString(leagueId));
            var text = await GetAsync(url, EspnFilter).ConfigureAwait(false);
            if (text.Error != null) return BaseResult.Failure<ProviderPositionsResult>(text.Error);

            var result = new ProviderPositionsResult { Success = true };
            try
            {
                using (var doc = JsonDocument.Parse(text.Body))
                {
                    JsonElement players;
                    if (!doc.RootElement.TryGetProperty("players", out players) || players.ValueKind != JsonValueKind.Array)
                        return BaseResult.Failure<ProviderPositionsResult>("ESPN returned no players.");

                    foreach (var item in players.EnumerateArray())
                    {
                        JsonElement p;
                        if (!item.TryGetProperty("player", out p)) continue;

                        var player = new ProviderPlayerPosition
                        {
                            ProviderPlayerId = Str(p, "id"),
                            Name = Str(p, "fullName")
                        };

                        int positionId;
                        string code;
                        if (int.TryParse(Str(p, "defaultPositionId"), out positionId) && EspnPositions.TryGetValue(positionId, out code))
                            player.Positions.Add(code);

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

        public async Task<ProviderPositionsResult> GetNflversePlayersAsync()
        {
            var text = await GetAsync(NflversePlayersUrl, null).ConfigureAwait(false);
            if (text.Error != null) return BaseResult.Failure<ProviderPositionsResult>(text.Error);

            var rows = ParseCsv(text.Body);
            if (rows.Count < 2) return BaseResult.Failure<ProviderPositionsResult>("nflverse players file is empty.");

            var header = rows[0];
            var idCol = Index(header, "gsis_id");
            var nameCol = Index(header, "display_name", "full_name", "name");
            var posCol = Index(header, "position");
            var teamCol = Index(header, "latest_team", "team_abbr", "team");

            if (idCol < 0 || posCol < 0)
                return BaseResult.Failure<ProviderPositionsResult>("nflverse players file is missing gsis_id or position.");

            var result = new ProviderPositionsResult { Success = true };
            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var id = Cell(row, idCol);
                var pos = Cell(row, posCol);
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(pos)) continue;

                var player = new ProviderPlayerPosition
                {
                    ProviderPlayerId = id.Trim(),
                    Name = Cell(row, nameCol),
                    Team = Cell(row, teamCol)
                };
                player.Positions.Add(pos.Trim());
                result.Players.Add(player);
            }

            return result;
        }

        private async Task<HttpText> GetAsync(string url, string espnFilter)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; RotoMonster)");
                    request.Headers.TryAddWithoutValidation("Accept", "application/json, text/csv, */*");
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

        private static int Index(List<string> header, params string[] names)
        {
            foreach (var n in names)
            {
                var i = header.FindIndex(h => string.Equals(h.Trim(), n, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) return i;
            }
            return -1;
        }

        private static string Cell(List<string> row, int index)
        {
            return index >= 0 && index < row.Count ? row[index] : null;
        }

        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }
                    continue;
                }

                if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                }
                else if (c == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else if (c != '\r')
                {
                    cell.Append(c);
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }

            return rows;
        }

        private class HttpText
        {
            public string Body;
            public string Error;
        }
    }
}
