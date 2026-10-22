using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Providers;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    public class OttoneuFantasyProvider : IFantasyProvider
    {
        private const string BaseUrl = "https://ottoneu.fangraphs.com/api/v1/";

        private static readonly HttpClient Http = new HttpClient();

        private static readonly HashSet<string> InactiveSlots =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "Bench", "Prospect", "Injury" };

        private readonly string _apiKey;
        private readonly string _sport;

        public OttoneuFantasyProvider(string apiKey, string sport = "basketball")
        {
            if (string.IsNullOrEmpty(apiKey)) throw new ArgumentNullException(nameof(apiKey));
            _apiKey = apiKey;
            _sport = string.IsNullOrWhiteSpace(sport) ? "basketball" : sport.Trim().ToLowerInvariant();
        }

        public string ProviderName
        {
            get { return "Ottoneu"; }
        }

        public static string LeaguesUrl(string username, string sport = "basketball")
        {
            return BaseUrl + sport + "/user/" + Uri.EscapeDataString((username ?? "").Trim().ToLowerInvariant());
        }

        public static string LeagueUrl(string leagueId, string sport = "basketball")
        {
            return BaseUrl + sport + "/league/" + Uri.EscapeDataString((leagueId ?? "").Trim());
        }

        public async Task<GetProviderLeaguesResult> GetLeaguesAsync(string userKey, string seasonKey)
        {
            if (string.IsNullOrWhiteSpace(userKey))
                return BaseResult.Failure<GetProviderLeaguesResult>("An Ottoneu username is required.");

            var response = await TryGet(LeaguesUrl(userKey, _sport)).ConfigureAwait(false);
            if (!response.Ok)
                return BaseResult.Failure<GetProviderLeaguesResult>(response.Error);

            return ParseLeagues(response.Body);
        }

        public static GetProviderLeaguesResult ParseLeagues(string json)
        {
            JsonElement root;
            if (!TryParse(json, out root) || root.ValueKind != JsonValueKind.Object)
                return BaseResult.Failure<GetProviderLeaguesResult>("Ottoneu returned a response we could not read.");

            var error = ApiError(root);
            if (error != null)
                return BaseResult.Failure<GetProviderLeaguesResult>(error);

            var result = new GetProviderLeaguesResult { Success = true };

            var teams = Prop(root, "teams");
            if (teams.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var node in teams.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;

                var leagueId = Str(node, "league_id");
                if (string.IsNullOrEmpty(leagueId)) continue;

                result.Leagues.Add(new ProviderLeague
                {
                    LeagueId = leagueId,
                    Title = Str(node, "league_name"),
                    MyTeamId = Str(node, "team_id"),
                    MyTeamTitle = Str(node, "team_name")
                });
            }

            return result;
        }

        public async Task<GetProviderLeagueDataResult> GetLeagueDataAsync(
            string userKey,
            string seasonKey,
            IList<string> leagueIds,
            ProviderLeagueDataParts parts)
        {
            var result = new GetProviderLeagueDataResult
            {
                Success = true,
                PartsReturned = parts & (ProviderLeagueDataParts.Settings | ProviderLeagueDataParts.Rosters)
            };

            if (leagueIds == null || leagueIds.Count == 0)
                return result;

            foreach (var leagueId in leagueIds)
            {
                var response = await TryGet(LeagueUrl(leagueId, _sport)).ConfigureAwait(false);
                result.RequestCount++;

                if (!response.Ok)
                {
                    result.Leagues.Add(new ProviderLeagueData { LeagueId = leagueId, ErrorMessage = response.Error });
                    continue;
                }

                result.Leagues.Add(ParseLeagueData(response.Body, leagueId, userKey, parts));
            }

            return result;
        }

        public static ProviderLeagueData ParseLeagueData(
            string json, string leagueId, string username, ProviderLeagueDataParts parts)
        {
            var entry = new ProviderLeagueData
            {
                LeagueId = leagueId,
                DraftPicks = new List<ProviderDraftPick>()
            };

            JsonElement root;
            if (!TryParse(json, out root) || root.ValueKind != JsonValueKind.Object)
            {
                entry.ErrorMessage = "Ottoneu returned a response we could not read.";
                return entry;
            }

            var error = ApiError(root);
            if (error != null)
            {
                entry.ErrorMessage = error;
                return entry;
            }

            var id = Str(root, "id");
            if (!string.IsNullOrEmpty(id)) entry.LeagueId = id;

            var teamsNode = Prop(root, "teams");

            if ((parts & ProviderLeagueDataParts.Settings) != 0)
                entry.Settings = ReadSettings(root, entry.LeagueId, teamsNode);

            if ((parts & ProviderLeagueDataParts.Rosters) != 0)
                entry.Teams = ReadTeams(teamsNode, entry.LeagueId, username);

            return entry;
        }

        private static ProviderLeagueSettings ReadSettings(JsonElement league, string leagueId, JsonElement teamsNode)
        {
            var settingsNode = Prop(league, "settings");
            var gameType = Prop(settingsNode, "game_type");
            var scoring = Str(gameType, "scoring_description");

            var settings = new ProviderLeagueSettings
            {
                LeagueId = leagueId,
                Title = Str(league, "name"),
                NumberOfTeams = Int(settingsNode, "num_teams"),
                ScoringSystem = scoring.ToLowerInvariant(),
                LineupFrequency = "daily",
                IsAuction = true,
                IsDynasty = true,
                HasDrafted = true,
                EntryFee = Int(settingsNode, "game_cost")
            };
            settings.IsMoney = settings.EntryFee > 0;

            if (teamsNode.ValueKind == JsonValueKind.Array && settings.NumberOfTeams == 0)
                settings.NumberOfTeams = teamsNode.GetArrayLength();

            var stats = Prop(gameType, "stat_set");
            if (stats.ValueKind == JsonValueKind.Array)
            {
                foreach (var stat in stats.EnumerateArray())
                {
                    var code = Str(stat, "stat_name");
                    if (string.IsNullOrEmpty(code)) continue;

                    settings.Categories.Add(new ProviderCategory
                    {
                        Code = code,
                        Name = Str(stat, "description")
                    });

                    var wantMost = Prop(stat, "want_most");
                    if (wantMost.ValueKind == JsonValueKind.False)
                        settings.Notes.Add(code + " is a lower-is-better category.");
                }
            }

            var lineup = Str(gameType, "lineup_description");
            if (!string.IsNullOrEmpty(lineup))
                settings.Notes.Add("Lineup: " + lineup);

            var playoffs = Str(Prop(settingsNode, "playoffs"), "description");
            if (!string.IsNullOrEmpty(playoffs))
                settings.Notes.Add("Playoffs: " + playoffs);

            return settings;
        }

        private static List<ProviderTeam> ReadTeams(JsonElement teamsNode, string leagueId, string username)
        {
            var teams = new List<ProviderTeam>();
            if (teamsNode.ValueKind != JsonValueKind.Array) return teams;

            var me = (username ?? "").Trim();

            foreach (var node in teamsNode.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;

                var team = new ProviderTeam
                {
                    LeagueId = leagueId,
                    TeamId = Str(node, "id"),
                    Title = Str(node, "name"),
                    IsMyTeam = me.Length > 0 && Owners(node).Contains(me, StringComparer.OrdinalIgnoreCase)
                };

                var roster = Prop(node, "roster");
                if (roster.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in roster.EnumerateArray())
                    {
                        var playerId = Str(p, "id");
                        if (string.IsNullOrEmpty(playerId)) continue;

                        var slot = Str(p, "last_fantasy_lineup_position");

                        team.Players.Add(new ProviderRosterPlayer
                        {
                            PlayerId = playerId,
                            AlternatePlayerId = IdOrEmpty(Str(p, "basketball_monster_id")),
                            Name = Str(p, "name"),
                            PositionCode = slot.Length > 0 ? slot : string.Join("/", Positions(p)),
                            IsActive = !InactiveSlots.Contains(slot),
                            IsIR = string.Equals(slot, "Injury", StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }

                teams.Add(team);
            }

            return teams;
        }

        private static string IdOrEmpty(string id)
        {
            return string.IsNullOrEmpty(id) || id == "0" ? "" : id;
        }

        private static IEnumerable<string> Owners(JsonElement team)
        {
            var owners = Prop(team, "owners");
            if (owners.ValueKind != JsonValueKind.Array) yield break;

            foreach (var o in owners.EnumerateArray())
            {
                var name = Str(o, "username");
                if (!string.IsNullOrEmpty(name)) yield return name;
            }
        }

        private static List<string> Positions(JsonElement player)
        {
            var list = new List<string>();
            var positions = Prop(player, "positions");
            if (positions.ValueKind != JsonValueKind.Array) return list;

            foreach (var t in positions.EnumerateArray())
                if (t.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(t.GetString()))
                    list.Add(t.GetString());

            return list;
        }

        private static string ApiError(JsonElement root)
        {
            var status = Prop(root, "status");
            if (status.ValueKind == JsonValueKind.Number && status.GetRawText() != "200")
            {
                var message = Str(root, "error");
                return "Ottoneu returned " + status.GetRawText() + (message.Length > 0 ? ": " + message : ".");
            }
            return null;
        }

        private struct Response
        {
            public bool Ok;
            public string Body;
            public string Error;
        }

        private async Task<Response> TryGet(string url)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Add("X-Api-Key", _apiKey);
                    var response = await Http.SendAsync(request).ConfigureAwait(false);
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if ((int)response.StatusCode == 404)
                        return new Response { Ok = false, Error = "Ottoneu could not find that user or league." };

                    if (!response.IsSuccessStatusCode)
                        return new Response { Ok = false, Error = "Ottoneu returned " + (int)response.StatusCode + "." };

                    if (string.IsNullOrWhiteSpace(body))
                        return new Response { Ok = false, Error = "Ottoneu returned nothing for that request." };

                    return new Response { Ok = true, Body = body };
                }
            }
            catch (Exception ex)
            {
                return new Response { Ok = false, Error = "Could not reach Ottoneu. [" + ex.Message + "]" };
            }
        }

        private static bool TryParse(string content, out JsonElement root)
        {
            root = default(JsonElement);
            if (string.IsNullOrWhiteSpace(content)) return false;

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

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";

            if (value.ValueKind == JsonValueKind.Number)
                return value.GetRawText();

            return "";
        }

        private static int Int(JsonElement node, string name)
        {
            var value = Prop(node, name);
            double number;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number))
                return (int)number;

            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number))
                return (int)number;

            return 0;
        }
    }
}
