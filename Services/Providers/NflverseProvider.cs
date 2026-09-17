using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Providers;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    public class NflverseProvider : ISportsDataProvider
    {
        private const string StatsUrl =
            "https://github.com/nflverse/nflverse-data/releases/download/stats_player/stats_player_week_{0}.csv";

        private static readonly Dictionary<string, string> Columns =
            new Dictionary<string, string>
            {
                { "attempts", "PassAttempts" },
                { "completions", "PassCompletions" },
                { "passing_yards", "PassYards" },
                { "passing_tds", "PassTD" },
                { "passing_interceptions", "PassInt" },
                { "sacks_suffered", "PassSacks" },
                { "sack_yards_lost", "PassSackYards" },
                { "passing_air_yards", "PassAirYards" },

                { "carries", "RushAttempts" },
                { "rushing_yards", "RushYards" },
                { "rushing_tds", "RushTD" },
                { "rushing_fumbles", "RushFumbles" },

                { "targets", "RecTargets" },
                { "receptions", "RecReceptions" },
                { "receiving_yards", "RecYards" },
                { "receiving_tds", "RecTD" },
                { "receiving_fumbles", "RecFumbles" },
                { "receiving_air_yards", "RecAirYards" },
                { "receiving_yards_after_catch", "RecYardsAfterCatch" },

                { "fumbles_total", "Fumbles" },
                { "fumbles_lost_total", "FumblesLost" },

                { "fg_att", "FieldGoals" },
                { "fg_made", "FieldGoalsMade" },
                { "fg_blocked", "FieldGoalsBlocked" },
                { "fg_long", "FieldGoalsLongest" },
                { "fg_made_0_19", "FieldGoals0to19" },
                { "fg_made_20_29", "FieldGoals20to29" },
                { "fg_made_30_39", "FieldGoals30to39" },
                { "fg_made_40_49", "FieldGoals40to49" },
                { "pat_att", "ExtraPointsAttempts" },
                { "pat_made", "ExtraPointsMade" },
                { "pat_blocked", "ExtraPointsBlocked" },

                { "def_sacks", "Sacks" },
                { "def_interceptions", "Interceptions" },
                { "fumble_recovery_opp", "FumbleRecoveries" },
                { "def_safeties", "Safeties" },
                { "def_tds", "InterceptionTouchdowns" },
                { "special_teams_tds", "ReturnTD" }
            };

        private static readonly Dictionary<string, string[]> Summed =
            new Dictionary<string, string[]>
            {
                { "FieldGoals50", new[] { "fg_made_50_59", "fg_made_60_" } },
                { "ReturnReturns", new[] { "punt_returns", "kickoff_returns" } },
                { "ReturnYards", new[] { "punt_return_yards", "kickoff_return_yards" } }
            };

        private readonly HttpClient _http;

        public NflverseProvider(HttpClient httpClient = null)
        {
            _http = httpClient ?? new HttpClient();
        }

        public string ProviderName { get { return "NFL"; } }

        public bool Supports(SportsDataSport sport)
        {
            return sport == SportsDataSport.NFL;
        }

        public Task<GetSportsDataGamesResult> GetGamesAsync(SportsDataSport sport, string season, string previousLastUpdated)
        {
            return Task.FromResult(BaseResult.Failure<GetSportsDataGamesResult>("nflverse is only used for player stats."));
        }

        public Task<GetSportsDataTeamsResult> GetTeamsAsync(SportsDataSport sport, string season, string previousLastUpdated)
        {
            return Task.FromResult(BaseResult.Failure<GetSportsDataTeamsResult>("nflverse is only used for player stats."));
        }

        public Task<GetSportsDataPlayersResult> GetPlayersAsync(SportsDataSport sport, string previousLastUpdated)
        {
            return Task.FromResult(BaseResult.Failure<GetSportsDataPlayersResult>("nflverse is only used for player stats."));
        }

        public Task<GetSportsDataPlayerGamesResult> GetPlayerGamesByDateAsync(SportsDataSport sport, string season, DateTime date, string previousLastUpdated)
        {
            return Task.FromResult(BaseResult.Failure<GetSportsDataPlayerGamesResult>("nflverse has no dates, use GetPlayerGamesAsync with a week."));
        }

        public async Task<GetSportsDataPlayerGamesResult> GetPlayerGamesAsync(
            SportsDataSport sport, string season, int week, string previousLastUpdated)
        {
            if (sport != SportsDataSport.NFL)
                return BaseResult.Failure<GetSportsDataPlayerGamesResult>("nflverse only covers the NFL.");

            int year;
            if (!TryGetYear(season, out year))
                return BaseResult.Failure<GetSportsDataPlayerGamesResult>("Could not read a year from season '" + season + "'.");

            var url = string.Format(CultureInfo.InvariantCulture, StatsUrl, year);

            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<GetSportsDataPlayerGamesResult>("nflverse request failed: " + ex.Message);
            }

            if (!response.IsSuccessStatusCode)
                return BaseResult.Failure<GetSportsDataPlayerGamesResult>("nflverse returned " + (int)response.StatusCode + " for " + url);

            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var lastModified = response.Content.Headers.LastModified.HasValue
                ? response.Content.Headers.LastModified.Value.ToString("o", CultureInfo.InvariantCulture)
                : null;

            var weekText = week.ToString(CultureInfo.InvariantCulture);
            var lines = new List<SportsDataPlayerGame>();

            var rows = ReadCsv(text);
            if (rows.Count == 0)
                return BaseResult.Failure<GetSportsDataPlayerGamesResult>("nflverse returned an empty file.");

            var header = rows[0];
            var index = new Dictionary<string, int>();
            for (var i = 0; i < header.Count; i++) index[header[i]] = i;

            var needed = new[] { "player_id", "week", "season_type", "game_id", "team", "position" };
            foreach (var column in needed)
                if (!index.ContainsKey(column))
                    return BaseResult.Failure<GetSportsDataPlayerGamesResult>("nflverse file is missing column " + column);

            for (var r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count < header.Count) continue;
                if (Cell(row, index, "week") != weekText) continue;
                if (Cell(row, index, "season_type") != "REG") continue;

                var playerId = Cell(row, index, "player_id");
                if (string.IsNullOrEmpty(playerId)) continue;

                var line = new SportsDataPlayerGame
                {
                    PlayerId = playerId,
                    GameId = Cell(row, index, "game_id"),
                    TeamCode = Cell(row, index, "team"),
                    Position = Cell(row, index, "position"),
                    PlayerName = index.ContainsKey("player_display_name") ? Cell(row, index, "player_display_name") : null
                };

                foreach (var map in Columns)
                {
                    double value;
                    if (TryNumber(row, index, map.Key, out value)) line.Stats[map.Value] = value;
                }

                foreach (var sum in Summed)
                {
                    double total = 0;
                    var any = false;
                    foreach (var column in sum.Value)
                    {
                        double value;
                        if (TryNumber(row, index, column, out value)) { total += value; any = true; }
                    }
                    if (any) line.Stats[sum.Key] = total;
                }

                lines.Add(line);
            }

            return new GetSportsDataPlayerGamesResult
            {
                Success = true,
                PlayerGames = lines,
                LastUpdatedOn = lastModified,
                NotModified = false
            };
        }

        public static string GameId(int year, int week, string awayCode, string homeCode)
        {
            return year.ToString(CultureInfo.InvariantCulture) + "_"
                   + week.ToString("00", CultureInfo.InvariantCulture) + "_"
                   + awayCode + "_" + homeCode;
        }

        private static bool TryGetYear(string season, out int year)
        {
            year = 0;
            if (string.IsNullOrEmpty(season) || season.Length < 4) return false;
            return int.TryParse(season.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out year);
        }

        private static string Cell(List<string> row, Dictionary<string, int> index, string column)
        {
            int i;
            if (!index.TryGetValue(column, out i) || i >= row.Count) return null;
            var value = row[i];
            return value == "NA" ? null : value;
        }

        private static bool TryNumber(List<string> row, Dictionary<string, int> index, string column, out double value)
        {
            value = 0;
            var text = Cell(row, index, column);
            if (string.IsNullOrEmpty(text)) return false;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static List<List<string>> ReadCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else quoted = false;
                    }
                    else field.Append(c);
                    continue;
                }

                if (c == '"') quoted = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\n')
                {
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else if (c != '\r') field.Append(c);
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows;
        }
    }
}
