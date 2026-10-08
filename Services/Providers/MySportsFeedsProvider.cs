using System.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Providers;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    /// <summary>
    /// MySportsFeeds, using the CORE, STATS and DETAILED addons.
    ///
    /// Auth is basic with the api key as the username and the literal word
    /// MYSPORTSFEEDS as the password, which is their own scheme rather than
    /// anything standard.
    ///
    /// Their feeds carry a lastUpdatedOn timestamp saying when the data last
    /// changed. Pass the previous one back in and an unchanged feed returns
    /// NotModified, so polling every minute during games costs one request and
    /// no reprocessing rather than rewriting the same rows every time.
    ///
    /// Their docs describe a 304 for this, but the API does not actually send
    /// one - no Last-Modified, no ETag, and If-Modified-Since is ignored - so
    /// the timestamp in the body is used instead.
    /// </summary>
    public class MySportsFeedsProvider : ISportsDataProvider
    {
        private const string BaseUrl = "https://api.mysportsfeeds.com/v2.1/pull/";

        private readonly string _apiKey;
        private readonly HttpClient _http;

        public MySportsFeedsProvider(string apiKey, HttpClient httpClient = null)
        {
            _apiKey = apiKey;
            _http = httpClient ?? new HttpClient();
        }

        public string ProviderName { get { return "MySportsFeeds"; } }

        public bool Supports(SportsDataSport sport)
        {
            return sport == SportsDataSport.NFL
                || sport == SportsDataSport.NBA
                || sport == SportsDataSport.MLB
                || sport == SportsDataSport.NHL;
        }

        public Task<GetSportsDataGamesResult> GetGamesAsync(
            SportsDataSport sport, string season, string previousLastUpdated)
        {
            return GetGamesFromPathAsync(SportPath(sport) + "/" + season + "/games.json", previousLastUpdated);
        }

        public Task<GetSportsDataGamesResult> GetGamesByDateAsync(
            SportsDataSport sport, string season, DateTime date)
        {
            return GetGamesFromPathAsync(
                SportPath(sport) + "/" + season + "/date/" + date.ToString("yyyyMMdd") + "/games.json", null);
        }

        private async Task<GetSportsDataGamesResult> GetGamesFromPathAsync(string path, string previousLastUpdated)
        {
            var fetch = await FetchAsync(path, previousLastUpdated).ConfigureAwait(false);

            if (!fetch.Success)
                return new GetSportsDataGamesResult
                {
                    Success = false,
                    ErrorMessage = fetch.ErrorMessage
                };

            if (fetch.NotModified)
                return new GetSportsDataGamesResult
                {
                    Success = true,
                    NotModified = true,
                    NoLiveAccess = fetch.NoLiveAccess,
                    LastUpdatedOn = fetch.LastUpdatedOn
                };

            var result = new GetSportsDataGamesResult
            {
                Success = true,
                LastUpdatedOn = fetch.LastUpdatedOn
            };

            try
            {
                using (var doc = JsonDocument.Parse(fetch.Body))
                {
                    JsonElement games;
                    if (!doc.RootElement.TryGetProperty("games", out games))
                        return result;

                    foreach (var entry in games.EnumerateArray())
                    {
                        JsonElement schedule;
                        if (!entry.TryGetProperty("schedule", out schedule)) continue;

                        var game = new SportsDataGame
                        {
                            GameId = ReadString(schedule, "id"),
                            StartTimeUtc = ReadDate(schedule, "startTime") ?? DateTime.MinValue,
                            Week = (int)ReadNumber(schedule, "week"),
                            Status = ReadString(schedule, "playedStatus")
                        };

                        JsonElement home, away;
                        if (schedule.TryGetProperty("homeTeam", out home))
                            game.HomeTeamCode = ReadString(home, "abbreviation");
                        if (schedule.TryGetProperty("awayTeam", out away))
                            game.AwayTeamCode = ReadString(away, "abbreviation");

                        JsonElement score;
                        if (entry.TryGetProperty("score", out score))
                        {
                            game.HomeScore = ReadNullableInt(score, "homeScoreTotal");
                            game.AwayScore = ReadNullableInt(score, "awayScoreTotal");
                            game.CurrentPeriod = ReadNullableInt(score, "currentQuarter");
                            game.PeriodSecondsRemaining = ReadNullableInt(score, "currentQuarterSecondsRemaining");
                            game.Intermission = ReadNullableInt(score, "currentIntermission");
                        }

                        var status = (game.Status ?? "").ToUpperInvariant();
                        game.IsFinished = status == "COMPLETED" || status == "COMPLETED_PENDING_REVIEW";
                        game.IsInProgress = status == "LIVE";

                        result.Games.Add(game);
                    }
                }
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<GetSportsDataGamesResult>(
                    "Could not read the games feed. " + ex.Message);
            }

            return result;
        }

        public async Task<GetSportsDataTeamsResult> GetTeamsAsync(
            SportsDataSport sport, string season, string previousLastUpdated)
        {
            var fetch = await FetchAsync(
                SportPath(sport) + "/" + season + "/standings.json", previousLastUpdated)
                .ConfigureAwait(false);

            if (!fetch.Success)
                return new GetSportsDataTeamsResult
                {
                    Success = false,
                    ErrorMessage = fetch.ErrorMessage
                };

            if (fetch.NotModified)
                return new GetSportsDataTeamsResult
                {
                    Success = true,
                    NotModified = true,
                    NoLiveAccess = fetch.NoLiveAccess,
                    LastUpdatedOn = fetch.LastUpdatedOn
                };

            var result = new GetSportsDataTeamsResult
            {
                Success = true,
                LastUpdatedOn = fetch.LastUpdatedOn
            };

            try
            {
                using (var doc = JsonDocument.Parse(fetch.Body))
                {
                    JsonElement standings;
                    if (!doc.RootElement.TryGetProperty("teams", out standings))
                        return result;

                    foreach (var entry in standings.EnumerateArray())
                    {
                        JsonElement team;
                        if (!entry.TryGetProperty("team", out team)) continue;

                        result.Teams.Add(new SportsDataTeam
                        {
                            TeamId = ReadString(team, "id"),
                            Code = ReadString(team, "abbreviation"),
                            City = ReadString(team, "city"),
                            Name = ReadString(team, "name")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<GetSportsDataTeamsResult>(
                    "Could not read the standings feed. " + ex.Message);
            }

            return result;
        }

        public async Task<GetSportsDataPlayersResult> GetPlayersAsync(
            SportsDataSport sport, string previousLastUpdated)
        {
            var fetch = await FetchAsync(
                SportPath(sport) + "/players.json", previousLastUpdated)
                .ConfigureAwait(false);

            if (!fetch.Success)
                return new GetSportsDataPlayersResult
                {
                    Success = false,
                    ErrorMessage = fetch.ErrorMessage
                };

            if (fetch.NotModified)
                return new GetSportsDataPlayersResult
                {
                    Success = true,
                    NotModified = true,
                    NoLiveAccess = fetch.NoLiveAccess,
                    LastUpdatedOn = fetch.LastUpdatedOn
                };

            var result = new GetSportsDataPlayersResult
            {
                Success = true,
                LastUpdatedOn = fetch.LastUpdatedOn
            };

            try
            {
                using (var doc = JsonDocument.Parse(fetch.Body))
                {
                    JsonElement players;
                    if (!doc.RootElement.TryGetProperty("players", out players))
                        return result;

                    foreach (var entry in players.EnumerateArray())
                    {
                        JsonElement p;
                        if (!entry.TryGetProperty("player", out p)) continue;

                        var player = new SportsDataPlayer
                        {
                            PlayerId = ReadString(p, "id"),
                            FirstName = ReadString(p, "firstName"),
                            LastName = ReadString(p, "lastName"),
                            Position = ReadString(p, "primaryPosition"),
                            BirthDate = ReadDate(p, "birthDate"),
                            HeightInches = ParseHeight(ReadString(p, "height")),
                            Weight = ReadNullableInt(p, "weight"),
                            College = ReadString(p, "college"),
                            RosterStatus = ReadString(p, "currentRosterStatus")
                        };

                        JsonElement team;
                        if (p.TryGetProperty("currentTeam", out team)
                            && team.ValueKind != JsonValueKind.Null)
                            player.TeamCode = ReadString(team, "abbreviation");

                        JsonElement drafted;
                        if (p.TryGetProperty("drafted", out drafted)
                            && drafted.ValueKind != JsonValueKind.Null)
                        {
                            player.RookieYear = ReadNullableInt(drafted, "year");
                            player.DraftPickNumber = ReadNullableInt(drafted, "overallPick");
                        }

                        JsonElement injury;
                        if (p.TryGetProperty("currentInjury", out injury)
                            && injury.ValueKind != JsonValueKind.Null)
                        {
                            player.Injury = new SportsDataInjury
                            {
                                Description = ReadString(injury, "description"),
                                PlayingProbability = ReadString(injury, "playingProbability")
                            };
                        }

                        result.Players.Add(player);
                    }
                }
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<GetSportsDataPlayersResult>(
                    "Could not read the players feed. " + ex.Message);
            }

            return result;
        }

        public Task<GetSportsDataPlayerGamesResult> GetPlayerGamesAsync(
            SportsDataSport sport, string season, int week, string previousLastUpdated)
        {
            var path = SportPath(sport) + "/" + season + "/week/"
                + week.ToString(CultureInfo.InvariantCulture) + "/player_gamelogs.json";

            return ReadPlayerGamesAsync(sport, path, previousLastUpdated);
        }

        public string CacheFolder { get; set; }

        public Task<GetSportsDataPlayerGamesResult> GetPlayerGamesByDateAsync(
            SportsDataSport sport, string season, DateTime date, string previousLastUpdated)
        {
            return GetPlayerGamesByDateAsync(sport, season, date, previousLastUpdated, false);
        }

        public Task<GetSportsDataPlayerGamesResult> GetPlayerGamesByDateAsync(
            SportsDataSport sport, string season, DateTime date, string previousLastUpdated, bool useStored)
        {
            var path = SportPath(sport) + "/" + season + "/date/"
                + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "/player_gamelogs.json";

            return ReadPlayerGamesAsync(sport, path, previousLastUpdated, useStored);
        }

        public bool HasStored(SportsDataSport sport, string season, DateTime date)
        {
            var path = SportPath(sport) + "/" + season + "/date/"
                + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "/player_gamelogs.json";
            var file = StoredFile(path);
            return file != null && System.IO.File.Exists(file);
        }

        private string StoredFile(string path)
        {
            if (string.IsNullOrWhiteSpace(CacheFolder)) return null;
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return System.IO.Path.Combine(new[] { CacheFolder }.Concat(parts).ToArray());
        }

        private string ReadStored(string path)
        {
            var file = StoredFile(path);
            if (file == null || !System.IO.File.Exists(file)) return null;
            try
            {
                return System.IO.File.ReadAllText(file);
            }
            catch (System.IO.IOException)
            {
                return null;
            }
        }

        private void Store(string path, string body)
        {
            var file = StoredFile(path);
            if (file == null || string.IsNullOrWhiteSpace(body)) return;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                var temp = file + ".tmp";
                System.IO.File.WriteAllText(temp, body);
                if (System.IO.File.Exists(file)) System.IO.File.Delete(file);
                System.IO.File.Move(temp, file);
            }
            catch (System.IO.IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private Task<GetSportsDataPlayerGamesResult> ReadPlayerGamesAsync(
            SportsDataSport sport, string path, string previousLastUpdated)
        {
            return ReadPlayerGamesAsync(sport, path, previousLastUpdated, false);
        }

        private async Task<GetSportsDataPlayerGamesResult> ReadPlayerGamesAsync(
            SportsDataSport sport, string path, string previousLastUpdated, bool useStored)
        {
            FetchResult fetch;
            var stored = useStored ? ReadStored(path) : null;

            if (stored != null)
            {
                fetch = new FetchResult { Success = true, Body = stored, LastUpdatedOn = ReadLastUpdated(stored) };
            }
            else
            {
                fetch = await FetchAsync(path, previousLastUpdated).ConfigureAwait(false);
                if (fetch.Success && !fetch.NotModified) Store(path, fetch.Body);
            }

            if (!fetch.Success)
                return new GetSportsDataPlayerGamesResult
                {
                    Success = false,
                    ErrorMessage = fetch.ErrorMessage
                };

            if (fetch.NotModified)
                return new GetSportsDataPlayerGamesResult
                {
                    Success = true,
                    NotModified = true,
                    NoLiveAccess = fetch.NoLiveAccess,
                    LastUpdatedOn = fetch.LastUpdatedOn
                };

            var map = MySportsFeedsStatMap.For(sport);
            var result = new GetSportsDataPlayerGamesResult
            {
                Success = true,
                LastUpdatedOn = fetch.LastUpdatedOn
            };

            try
            {
                using (var doc = JsonDocument.Parse(fetch.Body))
                {
                    JsonElement logs;
                    if (!doc.RootElement.TryGetProperty("gamelogs", out logs))
                        return result;

                    foreach (var entry in logs.EnumerateArray())
                    {
                        var line = new SportsDataPlayerGame();

                        JsonElement p, g, t;

                        if (entry.TryGetProperty("player", out p))
                        {
                            line.PlayerId = ReadString(p, "id");
                            line.Position = ReadString(p, "position");
                        }

                        if (entry.TryGetProperty("game", out g))
                            line.GameId = ReadString(g, "id");

                        if (entry.TryGetProperty("team", out t))
                            line.TeamCode = ReadString(t, "abbreviation");

                        JsonElement stats;
                        if (entry.TryGetProperty("stats", out stats)
                            && stats.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var group in stats.EnumerateObject())
                            {
                                if (group.Value.ValueKind != JsonValueKind.Object) continue;

                                foreach (var stat in group.Value.EnumerateObject())
                                {
                                    string ours;
                                    if (!map.TryGetValue(stat.Name, out ours)) continue;
                                    if (stat.Value.ValueKind != JsonValueKind.Number) continue;

                                    line.Stats[ours] = stat.Value.GetDouble();
                                }
                            }
                        }

                        if (sport == SportsDataSport.NFL) AddDerived(line);

                        result.PlayerGames.Add(line);
                    }
                }
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<GetSportsDataPlayerGamesResult>(
                    "Could not read the gamelogs feed. " + ex.Message);
            }

            return result;
        }

        private static void AddDerived(SportsDataPlayerGame line)
        {
            foreach (var pair in MySportsFeedsStatMap.NflDerived)
            {
                double total = 0;
                var any = false;

                foreach (var part in pair.Value)
                {
                    double value;
                    if (!line.Stats.TryGetValue(part, out value)) continue;
                    total += value;
                    any = true;
                }

                if (any) line.Stats[pair.Key] = total;
            }
        }

        private class FetchResult
        {
            public bool Success;
            public bool NotModified;
            public bool NoLiveAccess;
            public string Body;
            public string LastUpdatedOn;
            public string ErrorMessage;
        }

        private async Task<FetchResult> FetchAsync(string path, string previousLastUpdated)
        {
            var url = BaseUrl + path
                + (path.Contains("?") ? "&" : "?")
                + "force=true";

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    var raw = _apiKey + ":MYSPORTSFEEDS";
                    var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

                    using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.NotModified)
                            return new FetchResult { Success = true, NotModified = true };

                        if (response.StatusCode == HttpStatusCode.NoContent)
                            return new FetchResult { Success = true, NotModified = true, NoLiveAccess = true };

                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                            return new FetchResult
                            {
                                Success = false,
                                ErrorMessage = "MySportsFeeds returned "
                                    + (int)response.StatusCode + " for " + path
                            };

                        // An empty 200 means the same thing as a 304 on some of
                        // their feeds, so treat it the same rather than failing
                        // to parse nothing.
                        if (string.IsNullOrWhiteSpace(body))
                            return new FetchResult { Success = true, NotModified = true };

                        var stamp = ReadLastUpdated(body);

                        if (!string.IsNullOrEmpty(previousLastUpdated)
                            && !string.IsNullOrEmpty(stamp)
                            && stamp == previousLastUpdated)
                            return new FetchResult
                            {
                                Success = true,
                                NotModified = true,
                                LastUpdatedOn = stamp
                            };

                        return new FetchResult
                        {
                            Success = true,
                            Body = body,
                            LastUpdatedOn = stamp
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new FetchResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// Pulled out with a string scan rather than a full parse, because it
        /// is read on every response including the ones that turn out to have
        /// nothing new in them.
        /// </summary>
        private static string ReadLastUpdated(string body)
        {
            const string key = "\"lastUpdatedOn\":";

            var at = body.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return null;

            var open = body.IndexOf('"', at + key.Length);
            if (open < 0) return null;

            var close = body.IndexOf('"', open + 1);
            if (close < 0) return null;

            return body.Substring(open + 1, close - open - 1);
        }

        private static string SportPath(SportsDataSport sport)
        {
            if (sport == SportsDataSport.NBA) return "nba";
            if (sport == SportsDataSport.MLB) return "mlb";
            if (sport == SportsDataSport.NHL) return "nhl";
            return "nfl";
        }

        /// <summary>Their height is a string like 6'2".</summary>
        private static int? ParseHeight(string height)
        {
            if (string.IsNullOrEmpty(height)) return null;

            var parts = height.Replace("\"", "").Split('\'');
            if (parts.Length != 2) return null;

            int feet, inches;
            if (!int.TryParse(parts[0].Trim(), out feet)) return null;
            if (!int.TryParse(parts[1].Trim(), out inches)) return null;

            return (feet * 12) + inches;
        }

        private static string ReadString(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value)) return null;

            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind == JsonValueKind.Number)
                return value.GetDouble().ToString(CultureInfo.InvariantCulture);

            return null;
        }

        private static double ReadNumber(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value)) return 0;
            return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
        }

        private static int? ReadNullableInt(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value)) return null;
            if (value.ValueKind != JsonValueKind.Number) return null;

            int parsed;
            return value.TryGetInt32(out parsed) ? parsed : (int?)null;
        }

        private static DateTime? ReadDate(JsonElement element, string name)
        {
            var text = ReadString(element, name);
            if (string.IsNullOrEmpty(text)) return null;

            DateTime parsed;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed)
                ? parsed
                : (DateTime?)null;
        }
    }
}
