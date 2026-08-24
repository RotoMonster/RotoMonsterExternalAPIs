using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Providers;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    /// <summary>
    /// Sleeper as an IFantasyProvider.
    ///
    /// The easiest of the providers by some distance. The API is public and
    /// read only, so there is no OAuth, no cookies and no key. userKey here is
    /// the Sleeper user id, which the caller gets once from
    /// /v1/user/{username} and stores on UserAuth.
    ///
    /// Because nothing is authenticated, NeedsReauthorization is never set.
    ///
    /// There is no batch endpoint, so GetLeagueDataAsync loops. The interface
    /// says a provider without batching should loop internally rather than
    /// pushing it onto the caller, which is what happens here. RequestCount
    /// reports the real number so the difference from Yahoo stays visible.
    ///
    /// The sport is fixed per instance rather than passed in, because Sleeper
    /// scopes its league list by sport and the caller already knows which one
    /// it is importing.
    /// </summary>
    public class SleeperFantasyProvider : IFantasyProvider
    {
        private const string BaseUrl = "https://api.sleeper.app/v1/";

        private static readonly HttpClient Http = new HttpClient();

        /// <summary>Sleeper's sport code: nfl, nba, mlb or lcs.</summary>
        private readonly string _sport;

        public SleeperFantasyProvider(string sport)
        {
            if (string.IsNullOrEmpty(sport)) throw new ArgumentNullException(nameof(sport));
            _sport = sport.Trim().ToLowerInvariant();
        }

        public string ProviderName
        {
            get { return "Sleeper"; }
        }

        // -------------------------------------------------------------------
        // League list
        // -------------------------------------------------------------------

        public async Task<GetProviderLeaguesResult> GetLeaguesAsync(string userKey, string seasonKey)
        {
            if (string.IsNullOrEmpty(userKey))
                return BaseResult.Failure<GetProviderLeaguesResult>("A Sleeper user ID is required.");

            if (string.IsNullOrEmpty(seasonKey))
                return BaseResult.Failure<GetProviderLeaguesResult>("A season is required.");

            var url = BaseUrl + "user/" + userKey + "/leagues/" + _sport + "/" + seasonKey;

            var response = await TryGet(url).ConfigureAwait(false);
            if (!response.Ok)
                return BaseResult.Failure<GetProviderLeaguesResult>(response.Error);

            JsonElement root;
            if (!TryParse(response.Body, out root) || root.ValueKind != JsonValueKind.Array)
                return BaseResult.Failure<GetProviderLeaguesResult>("Sleeper returned a response we could not read.");

            var result = new GetProviderLeaguesResult { Success = true };

            foreach (var node in root.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;

                result.Leagues.Add(new ProviderLeague
                {
                    LeagueId = Str(node, "league_id"),
                    Title = Str(node, "name"),
                    SeasonKey = seasonKey
                    // MyTeamId is deliberately left empty. Sleeper's league list
                    // does not name the user's own roster, and finding it costs
                    // a /rosters call per league. The caller gets IsMyTeam on
                    // the teams when it asks for rosters, which is cheaper.
                });
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
            var result = new GetProviderLeagueDataResult
            {
                Success = true,
                PartsReturned = parts
            };

            if (leagueIds == null || leagueIds.Count == 0)
                return result;

            foreach (var leagueId in leagueIds)
            {
                var entry = new ProviderLeagueData { LeagueId = leagueId };
                result.Leagues.Add(entry);

                try
                {
                    // The league document carries the settings and the draft id,
                    // so it is fetched once and used for both parts.
                    var league = default(JsonElement);
                    var haveLeague = false;

                    var needsLeague = (parts & ProviderLeagueDataParts.Settings) != 0
                                   || (parts & ProviderLeagueDataParts.Drafts) != 0;

                    if (needsLeague)
                    {
                        var res = await TryGet(BaseUrl + "league/" + leagueId).ConfigureAwait(false);
                        result.RequestCount++;

                        if (!res.Ok)
                        {
                            entry.ErrorMessage = res.Error;
                            continue;
                        }

                        if (!TryParse(res.Body, out league) || league.ValueKind != JsonValueKind.Object)
                        {
                            entry.ErrorMessage = "Sleeper returned a league we could not read.";
                            continue;
                        }

                        haveLeague = true;
                    }

                    if ((parts & ProviderLeagueDataParts.Settings) != 0 && haveLeague)
                        entry.Settings = BuildSettings(leagueId, league);

                    if ((parts & ProviderLeagueDataParts.Rosters) != 0)
                        entry.Teams = await BuildTeams(leagueId, userKey, result).ConfigureAwait(false);

                    if ((parts & ProviderLeagueDataParts.Drafts) != 0 && haveLeague)
                    {
                        var draftId = Str(league, "draft_id");
                        entry.DraftPicks = await BuildDraftPicks(leagueId, draftId, result).ConfigureAwait(false);

                        // The auction budget and draft date only exist on the
                        // draft document, so fill them in once both are known.
                        if (entry.Settings != null && !string.IsNullOrEmpty(draftId))
                            await ApplyDraftSettings(entry.Settings, draftId, result).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    // One league failing should not lose the rest of the batch.
                    entry.ErrorMessage = ex.Message;
                }
            }

            return result;
        }

        // -------------------------------------------------------------------
        // Settings
        // -------------------------------------------------------------------

        private ProviderLeagueSettings BuildSettings(string leagueId, JsonElement league)
        {
            var status = Str(league, "status");

            var settings = new ProviderLeagueSettings
            {
                LeagueId = leagueId,
                Title = Str(league, "name"),
                NumberOfTeams = Int(league, "total_rosters"),

                // Sleeper is points scoring in every sport it supports. Left as
                // the provider's own wording, per the model.
                ScoringSystem = "points",

                // Sleeper runs weekly lineups for football and daily elsewhere.
                LineupFrequency = _sport == "nfl" ? "weekly" : "daily",

                // status runs pre_draft, drafting, in_season, complete.
                HasDrafted = !string.Equals(status, "pre_draft", StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(status, "drafting", StringComparison.OrdinalIgnoreCase)
            };

            // roster_positions is a flat array with one entry per slot, so the
            // same code appears several times and has to be counted.
            JsonElement positions;
            if (league.TryGetProperty("roster_positions", out positions)
                && positions.ValueKind == JsonValueKind.Array)
            {
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var token in positions.EnumerateArray())
                {
                    if (token.ValueKind != JsonValueKind.String) continue;

                    var code = (token.GetString() ?? "").Trim();
                    if (code.Length == 0) continue;

                    if (counts.ContainsKey(code)) counts[code]++;
                    else counts[code] = 1;
                }

                foreach (var pair in counts)
                {
                    var isBench = string.Equals(pair.Key, "BN", StringComparison.OrdinalIgnoreCase);
                    var isInjured = string.Equals(pair.Key, "IR", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(pair.Key, "TAXI", StringComparison.OrdinalIgnoreCase);

                    settings.RosterSpots.Add(new ProviderRosterSpot
                    {
                        Code = pair.Key,
                        Count = pair.Value,
                        IsBench = isBench,
                        IsInjured = isInjured
                    });

                    if (isInjured) settings.IRSpots += pair.Value;
                    else settings.PlayersPerTeam += pair.Value;
                }
            }

            // scoring_settings is an object of stat code to points per stat.
            JsonElement scoring;
            if (league.TryGetProperty("scoring_settings", out scoring)
                && scoring.ValueKind == JsonValueKind.Object)
            {
                foreach (var stat in scoring.EnumerateObject())
                {
                    var points = AsDouble(stat.Value);
                    if (!points.HasValue) continue;

                    settings.Categories.Add(new ProviderCategory
                    {
                        Code = stat.Name,
                        Name = stat.Name,
                        PointsPerStat = points.Value
                    });
                }
            }

            return settings;
        }

        private async Task ApplyDraftSettings(
            ProviderLeagueSettings settings, string draftId, GetProviderLeagueDataResult result)
        {
            var res = await TryGet(BaseUrl + "draft/" + draftId).ConfigureAwait(false);
            result.RequestCount++;
            if (!res.Ok) return; // the settings are still usable without it

            JsonElement draft;
            if (!TryParse(res.Body, out draft) || draft.ValueKind != JsonValueKind.Object)
                return;

            // type says "snake" or "auction" outright, which beats inferring
            // it. The budget check stays as a fallback in case an older draft
            // does not carry the type.
            var draftType = Str(draft, "type");

            JsonElement draftSettings;
            var budget = draft.TryGetProperty("settings", out draftSettings)
                         && draftSettings.ValueKind == JsonValueKind.Object
                ? Int(draftSettings, "budget")
                : 0;

            settings.IsAuction =
                string.Equals(draftType, "auction", StringComparison.OrdinalIgnoreCase)
                || budget > 0;

            // start_time is unix milliseconds.
            var epoch = AsDouble(Prop(draft, "start_time"));
            if (epoch.HasValue && epoch.Value > 0)
            {
                settings.DraftDate = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddMilliseconds(epoch.Value);
            }
        }

        // -------------------------------------------------------------------
        // Rosters
        // -------------------------------------------------------------------

        private async Task<List<ProviderTeam>> BuildTeams(
            string leagueId, string userKey, GetProviderLeagueDataResult result)
        {
            var teams = new List<ProviderTeam>();

            // Two calls: one names the teams, the other holds the players.
            var usersRes = await TryGet(BaseUrl + "league/" + leagueId + "/users").ConfigureAwait(false);
            result.RequestCount++;
            if (!usersRes.Ok) throw new Exception(usersRes.Error);

            var rostersRes = await TryGet(BaseUrl + "league/" + leagueId + "/rosters").ConfigureAwait(false);
            result.RequestCount++;
            if (!rostersRes.Ok) throw new Exception(rostersRes.Error);

            JsonElement users, rosters;
            if (!TryParse(usersRes.Body, out users) || users.ValueKind != JsonValueKind.Array
                || !TryParse(rostersRes.Body, out rosters) || rosters.ValueKind != JsonValueKind.Array)
                throw new Exception("Sleeper returned a roster we could not read.");

            var byOwner = new Dictionary<string, ProviderTeam>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in users.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;

                var ownerId = Str(node, "user_id");
                if (string.IsNullOrEmpty(ownerId)) continue;

                // A user who has named their team gets that, otherwise their
                // display name stands in.
                var title = Str(Prop(node, "metadata"), "team_name");
                if (string.IsNullOrEmpty(title)) title = Str(node, "display_name");

                var team = new ProviderTeam
                {
                    LeagueId = leagueId,
                    TeamId = ownerId,
                    Title = title,
                    IsMyTeam = string.Equals(ownerId, userKey, StringComparison.OrdinalIgnoreCase)
                };

                byOwner[ownerId] = team;
                teams.Add(team);
            }

            foreach (var node in rosters.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;

                var ownerId = Str(node, "owner_id");
                if (string.IsNullOrEmpty(ownerId)) continue;

                ProviderTeam team;
                if (!byOwner.TryGetValue(ownerId, out team)) continue;

                // starters is padded with "0" for slots nobody is in yet, so
                // it is only ever tested against a real player id rather than
                // iterated.
                var starters = SetOf(Prop(node, "starters"));
                var reserve = SetOf(Prop(node, "reserve"));

                var players = Prop(node, "players");
                if (players.ValueKind != JsonValueKind.Array) continue;

                foreach (var token in players.EnumerateArray())
                {
                    if (token.ValueKind != JsonValueKind.String) continue;

                    var playerId = token.GetString();
                    if (string.IsNullOrEmpty(playerId)) continue;

                    team.Players.Add(new ProviderRosterPlayer
                    {
                        PlayerId = playerId,
                        // Sleeper's roster endpoints give ids only. The caller
                        // matches on FantasyProviderPlayers, and the name is
                        // not available here to fall back on.
                        Name = null,
                        IsActive = starters.Contains(playerId),
                        IsIR = reserve.Contains(playerId)
                    });
                }
            }

            return teams;
        }

        // -------------------------------------------------------------------
        // Draft
        // -------------------------------------------------------------------

        private async Task<List<ProviderDraftPick>> BuildDraftPicks(
            string leagueId, string draftId, GetProviderLeagueDataResult result)
        {
            var picks = new List<ProviderDraftPick>();

            // A league that has not drafted yet has no draft id. Empty rather
            // than null, since the part was asked for.
            if (string.IsNullOrEmpty(draftId))
                return picks;

            var res = await TryGet(BaseUrl + "draft/" + draftId + "/picks").ConfigureAwait(false);
            result.RequestCount++;
            if (!res.Ok) throw new Exception(res.Error);

            JsonElement root;
            if (!TryParse(res.Body, out root) || root.ValueKind != JsonValueKind.Array)
                throw new Exception("Sleeper returned a draft we could not read.");

            foreach (var node in root.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;

                var pick = new ProviderDraftPick
                {
                    LeagueId = leagueId,
                    PlayerId = Str(node, "player_id"),
                    TeamId = Str(node, "picked_by"),
                    PickNumber = Int(node, "pick_no"),
                    Round = Int(node, "round")
                };

                var meta = Prop(node, "metadata");
                if (meta.ValueKind == JsonValueKind.Object)
                {
                    var name = (Str(meta, "first_name") + " " + Str(meta, "last_name")).Trim();
                    if (name.Length > 0) pick.PlayerName = name;

                    // amount is only present in an auction, and comes back as
                    // a string rather than a number.
                    var amount = AsDouble(Prop(meta, "amount"));
                    if (amount.HasValue) pick.Price = (int)amount.Value;
                }

                picks.Add(pick);
            }

            return picks;
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private struct Response
        {
            public bool Ok;
            public string Body;
            public string Error;
        }

        /// <summary>
        /// Sleeper answers an unknown id with an empty body or a literal null
        /// rather than an error document, so both are treated as a failure
        /// instead of an empty result.
        /// </summary>
        private static async Task<Response> TryGet(string url)
        {
            try
            {
                var response = await Http.GetAsync(url).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return new Response { Ok = false, Error = "Sleeper returned " + (int)response.StatusCode + "." };

                if (string.IsNullOrWhiteSpace(body) || body.Trim() == "null")
                    return new Response { Ok = false, Error = "Sleeper returned nothing for that request." };

                return new Response { Ok = true, Body = body };
            }
            catch (Exception ex)
            {
                return new Response { Ok = false, Error = "Could not reach Sleeper. [" + ex.Message + "]" };
            }
        }

        /// <summary>
        /// Clones the root element so it outlives the JsonDocument, which has
        /// to be disposed here rather than held open across the whole import.
        /// </summary>
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

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";

            if (value.ValueKind == JsonValueKind.Number)
                return value.GetRawText();

            return "";
        }

        private static int Int(JsonElement node, string name)
        {
            var value = AsDouble(Prop(node, name));
            return value.HasValue ? (int)value.Value : 0;
        }

        /// <summary>
        /// Sleeper is inconsistent about whether numbers arrive as numbers or
        /// strings, so both are accepted.
        /// </summary>
        private static double? AsDouble(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                double number;
                return value.TryGetDouble(out number) ? number : (double?)null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                double parsed;

                if (!string.IsNullOrEmpty(text)
                    && double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                    return parsed;
            }

            return null;
        }

        private static HashSet<string> SetOf(JsonElement array)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (array.ValueKind != JsonValueKind.Array) return set;

            foreach (var token in array.EnumerateArray())
            {
                if (token.ValueKind != JsonValueKind.String) continue;

                var value = token.GetString();
                if (!string.IsNullOrEmpty(value)) set.Add(value);
            }

            return set;
        }
    }
}
