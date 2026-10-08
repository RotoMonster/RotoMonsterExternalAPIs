using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Providers;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    public class NhlApiProvider : ISportsDataProvider
    {
        private const string BaseUrl = "https://api-web.nhle.com/v1/";
        private const string UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";

        public static readonly string[] TeamCodes =
        {
            "ANA", "BOS", "BUF", "CGY", "CAR", "CHI", "COL", "CBJ", "DAL", "DET", "EDM", "FLA", "LAK", "MIN", "MTL", "NSH",
            "NJD", "NYI", "NYR", "OTT", "PHI", "PIT", "SJS", "SEA", "STL", "TBL", "TOR", "UTA", "VAN", "VGK", "WSH", "WPG"
        };

        public static readonly string[] SkaterStats =
        {
            "Started",
            "PowerPlayTimeOnIce", "PowerPlayShots", "PowerPlayGoals", "PowerPlayMissedShots", "PowerPlayAssists", "PowerPlayFaceoffsWon", "PowerPlayFaceoffsLost",
            "ShorthandedTimeOnIce", "ShorthandedShots", "ShorthandedGoals", "ShorthandedMissedShots", "ShorthandedAssists", "ShorthandedFaceoffsWon", "ShorthandedFaceoffsLost",
            "EvenstrengthTimeOnIce", "EvenstrengthShots", "EvenstrengthGoals", "EvenstrengthMissedShots", "EvenstrengthAssists", "EvenstrengthFaceoffsWon", "EvenstrengthFaceoffsLost",
            "PenaltyShots", "PenaltyGoals", "PenaltyMissedShots", "ShootoutShots", "ShootoutGoals", "ShootoutMissedShots",
            "Penalties", "PenaltyMinutes", "BlockedAttempts", "Hits", "Giveaways", "Takeaways", "BlockedShots", "PlusMinus",
            "OvertimeGoals", "OvertimeAssists", "OvertimeShots",
            "PenaltiesMajor", "PenaltiesMinor", "PenaltiesMisconduct", "EmptynetGoals", "Shifts"
        };

        private readonly HttpClient _http;

        public NhlApiProvider(HttpClient httpClient = null)
        {
            _http = httpClient ?? new HttpClient();
        }

        public string ProviderName { get { return "NHL"; } }

        public bool Supports(SportsDataSport sport)
        {
            return sport == SportsDataSport.NHL;
        }

        public async Task<GetSportsDataGamesResult> GetGamesAsync(SportsDataSport sport, string season, string previousLastUpdated)
        {
            if (sport != SportsDataSport.NHL)
                return BaseResult.Failure<GetSportsDataGamesResult>("The NHL API only serves the NHL.");

            var games = new Dictionary<string, SportsDataGame>();
            var bodies = new StringBuilder();
            foreach (var team in TeamCodes)
            {
                string body;
                try
                {
                    body = await GetStringAsync("club-schedule-season/" + team + "/" + season).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return BaseResult.Failure<GetSportsDataGamesResult>("Could not read the " + team + " schedule. " + ex.Message);
                }
                bodies.Append(body);
                using (var doc = JsonDocument.Parse(body))
                {
                    JsonElement list;
                    if (!doc.RootElement.TryGetProperty("games", out list)) continue;
                    foreach (var g in list.EnumerateArray())
                    {
                        if (ReadInt(g, "gameType") != 2) continue;
                        var game = ReadGame(g);
                        if (game.GameId != null && !games.ContainsKey(game.GameId))
                            games[game.GameId] = game;
                    }
                }
            }

            return GamesResult(games.Values.OrderBy(x => x.StartTimeUtc).ToList(), Fingerprint(bodies.ToString()), previousLastUpdated);
        }

        public async Task<GetSportsDataGamesResult> GetGamesByDateAsync(SportsDataSport sport, string season, DateTime date, string previousLastUpdated = null)
        {
            if (sport != SportsDataSport.NHL)
                return BaseResult.Failure<GetSportsDataGamesResult>("The NHL API only serves the NHL.");

            string body;
            try
            {
                body = await GetStringAsync("score/" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<GetSportsDataGamesResult>("Could not read the scores for " + date.ToString("M/d") + ". " + ex.Message);
            }

            var games = new List<SportsDataGame>();
            using (var doc = JsonDocument.Parse(body))
            {
                JsonElement list;
                if (doc.RootElement.TryGetProperty("games", out list))
                {
                    foreach (var g in list.EnumerateArray())
                    {
                        if (ReadInt(g, "gameType") != 2) continue;
                        games.Add(ReadGame(g));
                    }
                }
            }

            return GamesResult(games, Fingerprint(body), previousLastUpdated);
        }

        public async Task<GetSportsDataTeamsResult> GetTeamsAsync(SportsDataSport sport, string season, string previousLastUpdated)
        {
            if (sport != SportsDataSport.NHL)
                return BaseResult.Failure<GetSportsDataTeamsResult>("The NHL API only serves the NHL.");

            string body;
            try
            {
                body = await GetStringAsync("standings/now").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<GetSportsDataTeamsResult>("Could not read the standings. " + ex.Message);
            }

            var result = new GetSportsDataTeamsResult { Success = true, LastUpdatedOn = Fingerprint(body) };
            using (var doc = JsonDocument.Parse(body))
            {
                JsonElement list;
                if (doc.RootElement.TryGetProperty("standings", out list))
                {
                    foreach (var t in list.EnumerateArray())
                    {
                        var code = ReadName(t, "teamAbbrev");
                        result.Teams.Add(new SportsDataTeam
                        {
                            TeamId = code,
                            Code = code,
                            City = ReadName(t, "placeName"),
                            Name = ReadName(t, "teamCommonName")
                        });
                    }
                }
            }
            return result;
        }

        public async Task<GetSportsDataPlayersResult> GetPlayersAsync(SportsDataSport sport, string previousLastUpdated)
        {
            if (sport != SportsDataSport.NHL)
                return BaseResult.Failure<GetSportsDataPlayersResult>("The NHL API only serves the NHL.");

            var players = new Dictionary<string, SportsDataPlayer>();
            var bodies = new StringBuilder();
            foreach (var team in TeamCodes)
            {
                string body;
                try
                {
                    body = await GetStringAsync("roster/" + team + "/current").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return BaseResult.Failure<GetSportsDataPlayersResult>("Could not read the " + team + " roster. " + ex.Message);
                }
                bodies.Append(body);
                using (var doc = JsonDocument.Parse(body))
                {
                    foreach (var group in new[] { "forwards", "defensemen", "goalies" })
                    {
                        JsonElement list;
                        if (!doc.RootElement.TryGetProperty(group, out list)) continue;
                        foreach (var p in list.EnumerateArray())
                        {
                            var id = ReadString(p, "id");
                            if (id == null || players.ContainsKey(id)) continue;
                            players[id] = new SportsDataPlayer
                            {
                                PlayerId = id,
                                FirstName = ReadName(p, "firstName"),
                                LastName = ReadName(p, "lastName"),
                                Position = ReadString(p, "positionCode"),
                                TeamCode = team,
                                BirthDate = ReadDate(p, "birthDate"),
                                HeightInches = ReadNullableInt(p, "heightInInches"),
                                Weight = ReadNullableInt(p, "weightInPounds"),
                                RosterStatus = "ROSTER"
                            };
                        }
                    }
                }
            }

            var lastUpdated = Fingerprint(bodies.ToString());
            if (previousLastUpdated != null && previousLastUpdated == lastUpdated)
                return new GetSportsDataPlayersResult { Success = true, NotModified = true, LastUpdatedOn = lastUpdated };

            var result = new GetSportsDataPlayersResult { Success = true, LastUpdatedOn = lastUpdated };
            result.Players.AddRange(players.Values);
            return result;
        }

        public Task<GetSportsDataPlayerGamesResult> GetPlayerGamesAsync(SportsDataSport sport, string season, int week, string previousLastUpdated)
        {
            return Task.FromResult(BaseResult.Failure<GetSportsDataPlayerGamesResult>(
                "The NHL has no weeks. Use GetPlayerGamesByDateAsync."));
        }

        public async Task<GetSportsDataPlayerGamesResult> GetPlayerGamesByDateAsync(SportsDataSport sport, string season, DateTime date, string previousLastUpdated)
        {
            if (sport != SportsDataSport.NHL)
                return BaseResult.Failure<GetSportsDataPlayerGamesResult>("The NHL API only serves the NHL.");

            var day = await GetGamesByDateAsync(sport, season, date, null).ConfigureAwait(false);
            if (!day.Success)
                return BaseResult.Failure<GetSportsDataPlayerGamesResult>(day.ErrorMessage);

            var lines = new List<SportsDataPlayerGame>();
            var bodies = new StringBuilder();
            foreach (var game in day.Games.Where(g => g.IsInProgress || g.IsFinished))
            {
                string box, pbp;
                try
                {
                    box = await GetStringAsync("gamecenter/" + game.GameId + "/boxscore").ConfigureAwait(false);
                    pbp = await GetStringAsync("gamecenter/" + game.GameId + "/play-by-play").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return BaseResult.Failure<GetSportsDataPlayerGamesResult>("Could not read game " + game.GameId + ". " + ex.Message);
                }
                bodies.Append(box).Append(pbp);
                lines.AddRange(ReadPlayerGames(game, box, pbp));
            }

            var lastUpdated = Fingerprint(bodies.ToString());
            if (previousLastUpdated != null && previousLastUpdated == lastUpdated)
                return new GetSportsDataPlayerGamesResult { Success = true, NotModified = true, LastUpdatedOn = lastUpdated };

            var result = new GetSportsDataPlayerGamesResult { Success = true, LastUpdatedOn = lastUpdated };
            result.PlayerGames.AddRange(lines);
            return result;
        }

        private static List<SportsDataPlayerGame> ReadPlayerGames(SportsDataGame game, string boxJson, string pbpJson)
        {
            var skaters = new Dictionary<string, SportsDataPlayerGame>();
            var isHome = new Dictionary<string, bool>();
            var goalies = new List<SportsDataPlayerGame>();

            using (var box = JsonDocument.Parse(boxJson))
            {
                var root = box.RootElement;
                var homeScore = ReadNullableInt(Child(root, "homeTeam"), "score") ?? 0;
                var awayScore = ReadNullableInt(Child(root, "awayTeam"), "score") ?? 0;
                var periodType = ReadString(Child(root, "periodDescriptor"), "periodType") ?? "REG";
                var beyondRegulation = periodType == "OT" || periodType == "SO";

                JsonElement stats;
                if (!root.TryGetProperty("playerByGameStats", out stats)) return new List<SportsDataPlayerGame>();

                foreach (var side in new[] { "homeTeam", "awayTeam" })
                {
                    var home = side == "homeTeam";
                    var teamCode = home ? game.HomeTeamCode : game.AwayTeamCode;
                    JsonElement sideStats;
                    if (!stats.TryGetProperty(side, out sideStats)) continue;

                    foreach (var group in new[] { "forwards", "defense", "defensemen" })
                    {
                        JsonElement list;
                        if (!sideStats.TryGetProperty(group, out list)) continue;
                        foreach (var p in list.EnumerateArray())
                        {
                            var id = ReadString(p, "playerId");
                            if (id == null || skaters.ContainsKey(id)) continue;
                            var line = new SportsDataPlayerGame
                            {
                                PlayerId = id,
                                GameId = game.GameId,
                                TeamCode = teamCode,
                                Position = ReadString(p, "position"),
                                PlayerName = ReadName(p, "name")
                            };
                            foreach (var name in SkaterStats) line.Stats[name] = 0;
                            line.Stats["Hits"] = ReadNumber(p, "hits");
                            line.Stats["BlockedShots"] = ReadNumber(p, "blockedShots");
                            line.Stats["Giveaways"] = ReadNumber(p, "giveaways");
                            line.Stats["Takeaways"] = ReadNumber(p, "takeaways");
                            line.Stats["PlusMinus"] = ReadNumber(p, "plusMinus");
                            line.Stats["Shifts"] = ReadNumber(p, "shifts");
                            line.Stats["PenaltyMinutes"] = ReadNumber(p, "pim");
                            line.Stats["EvenstrengthTimeOnIce"] = Minutes(ReadString(p, "toi"));
                            skaters[id] = line;
                            isHome[id] = home;
                        }
                    }

                    var won = home ? homeScore > awayScore : awayScore > homeScore;
                    JsonElement goalieList;
                    if (!sideStats.TryGetProperty("goalies", out goalieList)) continue;
                    foreach (var g in goalieList.EnumerateArray())
                    {
                        var started = ReadBool(g, "starter");
                        var toi = Minutes(ReadString(g, "toi"));
                        if (toi <= 0 && !started) continue;

                        var ev = SplitShots(ReadString(g, "evenStrengthShotsAgainst"));
                        var pp = SplitShots(ReadString(g, "powerPlayShotsAgainst"));
                        var sh = SplitShots(ReadString(g, "shorthandedShotsAgainst"));
                        var goalsAgainst = ReadNumber(g, "goalsAgainst");
                        var final = game.IsFinished;

                        var line = new SportsDataPlayerGame
                        {
                            PlayerId = ReadString(g, "playerId"),
                            GameId = game.GameId,
                            TeamCode = teamCode,
                            Position = "G",
                            PlayerName = ReadName(g, "name")
                        };
                        line.Stats["Started"] = started ? 1 : 0;
                        line.Stats["Shifts"] = 0;
                        line.Stats["Assists"] = 0;
                        line.Stats["CreditWin"] = final && started && won ? 1 : 0;
                        line.Stats["CreditOvertimeLoss"] = final && started && !won && beyondRegulation ? 1 : 0;
                        line.Stats["CreditLoss"] = final && started && !won && !beyondRegulation ? 1 : 0;
                        line.Stats["Wins"] = final && started && won ? 1 : 0;
                        line.Stats["Shutouts"] = final && started && goalsAgainst == 0 && toi >= 55 ? 1 : 0;
                        line.Stats["PowerPlayTimeOnIce"] = 0;
                        line.Stats["PowerPlayShotsAgainst"] = pp.Item2;
                        line.Stats["PowerPlayGoalsAgainst"] = pp.Item1;
                        line.Stats["PowerPlaySaves"] = pp.Item2 - pp.Item1;
                        line.Stats["ShorthandedTimeOnIce"] = 0;
                        line.Stats["ShorthandedShotsAgainst"] = sh.Item2;
                        line.Stats["ShorthandedGoalsAgainst"] = sh.Item1;
                        line.Stats["ShorthandedPlaySaves"] = sh.Item2 - sh.Item1;
                        line.Stats["EvenstrengthTimeOnIce"] = toi;
                        line.Stats["EvenstrengthShotsAgainst"] = ev.Item2;
                        line.Stats["EvenstrengthGoalsAgainst"] = ev.Item1;
                        line.Stats["EvenstrengthPlaySaves"] = ev.Item2 - ev.Item1;
                        line.Stats["PenaltyShotsAgainst"] = 0;
                        line.Stats["PenaltyGoalsAgainst"] = 0;
                        line.Stats["PenaltySaves"] = 0;
                        line.Stats["ShootoutShotsAgainst"] = 0;
                        line.Stats["ShootoutGoalsAgainst"] = 0;
                        line.Stats["ShootoutSaves"] = 0;
                        goalies.Add(line);
                    }
                }
            }

            using (var pbp = JsonDocument.Parse(pbpJson))
            {
                JsonElement plays;
                if (pbp.RootElement.TryGetProperty("plays", out plays))
                {
                    foreach (var play in plays.EnumerateArray())
                        ApplyPlay(play, skaters, isHome);
                }
            }

            var all = skaters.Values.ToList();
            all.AddRange(goalies);
            return all;
        }

        private static void ApplyPlay(JsonElement play, Dictionary<string, SportsDataPlayerGame> skaters, Dictionary<string, bool> isHome)
        {
            var kind = ReadString(play, "typeDescKey");
            var details = Child(play, "details");
            var descriptor = Child(play, "periodDescriptor");
            var period = ReadNullableInt(descriptor, "number") ?? 1;
            var periodType = ReadString(descriptor, "periodType") ?? "REG";
            var situation = ReadString(play, "situationCode");
            var overtime = periodType == "OT" || period >= 4;
            var shootout = periodType == "SO";

            if (kind == "goal")
            {
                var scorer = ReadString(details, "scoringPlayerId");
                if (scorer != null)
                {
                    if (shootout)
                    {
                        Bump(skaters, scorer, "ShootoutGoals");
                        Bump(skaters, scorer, "ShootoutShots");
                    }
                    else
                    {
                        var prefix = Prefix(situation, scorer, isHome);
                        Bump(skaters, scorer, prefix + "Goals");
                        Bump(skaters, scorer, prefix + "Shots");
                        if (overtime)
                        {
                            Bump(skaters, scorer, "OvertimeGoals");
                            Bump(skaters, scorer, "OvertimeShots");
                        }
                        if (situation != null && situation.Length == 4)
                        {
                            bool home;
                            isHome.TryGetValue(scorer, out home);
                            var opposingGoalie = home ? situation[0] : situation[3];
                            if (opposingGoalie == '0') Bump(skaters, scorer, "EmptynetGoals");
                        }
                    }
                }
                foreach (var key in new[] { "assist1PlayerId", "assist2PlayerId" })
                {
                    var assist = ReadString(details, key);
                    if (assist == null) continue;
                    Bump(skaters, assist, Prefix(situation, assist, isHome) + "Assists");
                    if (overtime) Bump(skaters, assist, "OvertimeAssists");
                }
            }
            else if (kind == "shot-on-goal")
            {
                var shooter = ReadString(details, "shootingPlayerId");
                if (shooter == null) return;
                if (shootout)
                {
                    Bump(skaters, shooter, "ShootoutShots");
                }
                else
                {
                    Bump(skaters, shooter, Prefix(situation, shooter, isHome) + "Shots");
                    if (overtime) Bump(skaters, shooter, "OvertimeShots");
                }
            }
            else if (kind == "missed-shot")
            {
                var shooter = ReadString(details, "shootingPlayerId");
                if (shooter == null) return;
                Bump(skaters, shooter, shootout ? "ShootoutMissedShots" : Prefix(situation, shooter, isHome) + "MissedShots");
            }
            else if (kind == "blocked-shot")
            {
                var shooter = ReadString(details, "shootingPlayerId");
                if (shooter != null) Bump(skaters, shooter, "BlockedAttempts");
            }
            else if (kind == "faceoff")
            {
                var winner = ReadString(details, "winningPlayerId");
                var loser = ReadString(details, "losingPlayerId");
                if (winner != null) Bump(skaters, winner, Prefix(situation, winner, isHome) + "FaceoffsWon");
                if (loser != null) Bump(skaters, loser, Prefix(situation, loser, isHome) + "FaceoffsLost");
            }
            else if (kind == "penalty")
            {
                var player = ReadString(details, "committedByPlayerId") ?? ReadString(details, "servedByPlayerId");
                if (player == null) return;
                Bump(skaters, player, "Penalties");
                var type = ReadString(details, "typeCode") ?? "MIN";
                if (type == "MAJ") Bump(skaters, player, "PenaltiesMajor");
                else if (type == "MIS" || type == "GMIS") Bump(skaters, player, "PenaltiesMisconduct");
                else Bump(skaters, player, "PenaltiesMinor");
            }
        }

        private static string Prefix(string situation, string playerId, Dictionary<string, bool> isHome)
        {
            if (situation == null || situation.Length != 4) return "Evenstrength";
            int awaySkaters, homeSkaters;
            if (!int.TryParse(situation.Substring(1, 1), out awaySkaters) || !int.TryParse(situation.Substring(2, 1), out homeSkaters))
                return "Evenstrength";
            bool home;
            isHome.TryGetValue(playerId, out home);
            var mine = home ? homeSkaters : awaySkaters;
            var theirs = home ? awaySkaters : homeSkaters;
            if (mine > theirs) return "PowerPlay";
            if (mine < theirs) return "Shorthanded";
            return "Evenstrength";
        }

        private static void Bump(Dictionary<string, SportsDataPlayerGame> skaters, string playerId, string stat)
        {
            SportsDataPlayerGame line;
            if (!skaters.TryGetValue(playerId, out line)) return;
            double value;
            line.Stats.TryGetValue(stat, out value);
            line.Stats[stat] = value + 1;
        }

        private static Tuple<int, int> SplitShots(string value)
        {
            if (string.IsNullOrEmpty(value)) return Tuple.Create(0, 0);
            var parts = value.Split('/');
            int saves, shots;
            if (parts.Length != 2 || !int.TryParse(parts[0], out saves) || !int.TryParse(parts[1], out shots))
                return Tuple.Create(0, 0);
            return Tuple.Create(shots - saves, shots);
        }

        private static double Minutes(string toi)
        {
            if (string.IsNullOrEmpty(toi)) return 0;
            var parts = toi.Split(':');
            int m, s;
            if (parts.Length != 2 || !int.TryParse(parts[0], out m) || !int.TryParse(parts[1], out s)) return 0;
            return Math.Round(m + s / 60.0, 4);
        }

        private static SportsDataGame ReadGame(JsonElement g)
        {
            var home = Child(g, "homeTeam");
            var away = Child(g, "awayTeam");
            var clock = Child(g, "clock");
            var state = ReadString(g, "gameState") ?? "";

            var game = new SportsDataGame
            {
                GameId = ReadString(g, "id"),
                StartTimeUtc = ReadDate(g, "startTimeUTC") ?? DateTime.MinValue,
                Week = 0,
                HomeTeamCode = ReadString(home, "abbrev"),
                AwayTeamCode = ReadString(away, "abbrev"),
                HomeScore = ReadNullableInt(home, "score"),
                AwayScore = ReadNullableInt(away, "score"),
                Status = state,
                IsFinished = state == "OFF" || state == "FINAL",
                IsInProgress = state == "LIVE" || state == "CRIT",
                CurrentPeriod = ReadNullableInt(Child(g, "periodDescriptor"), "number") ?? ReadNullableInt(g, "period"),
                PeriodSecondsRemaining = ReadNullableInt(clock, "secondsRemaining"),
                Intermission = ReadBool(clock, "inIntermission") ? 1 : (int?)null
            };
            if (game.StartTimeUtc != DateTime.MinValue)
                game.StartTimeUtc = DateTime.SpecifyKind(game.StartTimeUtc.ToUniversalTime(), DateTimeKind.Utc);
            return game;
        }

        private static GetSportsDataGamesResult GamesResult(List<SportsDataGame> games, string lastUpdated, string previousLastUpdated)
        {
            if (previousLastUpdated != null && previousLastUpdated == lastUpdated)
                return new GetSportsDataGamesResult { Success = true, NotModified = true, LastUpdatedOn = lastUpdated };
            var result = new GetSportsDataGamesResult { Success = true, LastUpdatedOn = lastUpdated };
            result.Games.AddRange(games);
            return result;
        }

        private async Task<string> GetStringAsync(string path)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        private static string Fingerprint(string body)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(body ?? ""));
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        private static JsonElement Child(JsonElement parent, string name)
        {
            JsonElement value;
            if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out value)) return value;
            return default(JsonElement);
        }

        private static string ReadString(JsonElement parent, string name)
        {
            var value = Child(parent, name);
            switch (value.ValueKind)
            {
                case JsonValueKind.String: return value.GetString();
                case JsonValueKind.Number: return value.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return null;
            }
        }

        private static string ReadName(JsonElement parent, string name)
        {
            var value = Child(parent, name);
            if (value.ValueKind == JsonValueKind.Object) return ReadString(value, "default");
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            return null;
        }

        private static double ReadNumber(JsonElement parent, string name)
        {
            var value = Child(parent, name);
            double number;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number)) return number;
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number)) return number;
            return 0;
        }

        private static int? ReadNullableInt(JsonElement parent, string name)
        {
            var value = Child(parent, name);
            int number;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number)) return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
            return null;
        }

        private static int ReadInt(JsonElement parent, string name)
        {
            return ReadNullableInt(parent, name) ?? 0;
        }

        private static bool ReadBool(JsonElement parent, string name)
        {
            var value = Child(parent, name);
            return value.ValueKind == JsonValueKind.True;
        }

        private static DateTime? ReadDate(JsonElement parent, string name)
        {
            var text = ReadString(parent, name);
            DateTime parsed;
            if (text != null && DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                return parsed;
            return null;
        }
    }
}
