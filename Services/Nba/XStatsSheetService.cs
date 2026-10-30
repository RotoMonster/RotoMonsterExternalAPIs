using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Nba;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services.Nba
{
    public class XStatsSheetService
    {
        public const string DefaultSheetId = "1ZI6my14lS5aEdvUUi90hX0_VCp9rYLR665v2Z9auzsE";
        public const string DefaultSeasonPrefix = "00226";
        public const string DefaultGid = "2100003001";

        private readonly HttpClient _http;

        public XStatsSheetService(HttpClient http)
        {
            _http = http;
        }

        public static string CsvUrl(string sheetId, string gid)
        {
            return $"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=csv&gid={gid}";
        }

        public async Task<GetXStatsResult> GetSeasonStatsAsync(string gid = DefaultGid, string sheetId = DefaultSheetId, string seasonPrefix = DefaultSeasonPrefix, CancellationToken ct = default(CancellationToken))
        {
            var url = CsvUrl(sheetId, gid);
            try
            {
                using (var response = await _http.GetAsync(url, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        return BaseResult.Failure<GetXStatsResult>($"{url} returned {(int)response.StatusCode}");
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    var result = Calculate(Encoding.UTF8.GetString(bytes), seasonPrefix);
                    result.Url = url;
                    return result;
                }
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<GetXStatsResult>(ex.Message);
            }
        }

        public static GetXStatsResult Calculate(string csv, string seasonPrefix = DefaultSeasonPrefix)
        {
            var result = new GetXStatsResult();
            var rows = ParseCsv(csv);
            if (rows.Count < 2)
                return BaseResult.Failure<GetXStatsResult>("The sheet came back empty.");

            var header = rows[0].Select(h => h.Trim()).ToList();
            var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count; i++)
                if (!col.ContainsKey(header[i])) col[header[i]] = i;

            var required = new[] { "gameId", "gameDate", "teamAbbreviation", "playerId", "playerName", "minutes", "boxFga", "box3pa", "boxFta",
                "trackedFga", "tracked2pa", "tracked3pa", "x2Makes", "x3Makes", "fullyExpectedPoints", "xRebounds", "xAssists", "xSteals", "xBlocks", "xTurnovers", "xFtm" };
            var missing = required.Where(r => !col.ContainsKey(r)).ToList();
            if (missing.Count > 0)
                return BaseResult.Failure<GetXStatsResult>("Missing columns: " + string.Join(", ", missing) + ". Is this the per-game expected stats tab?");

            Func<List<string>, string, string> get = (row, name) => col[name] < row.Count ? row[col[name]] : "";

            var latest = new Dictionary<string, List<string>>();
            foreach (var row in rows.Skip(1))
            {
                var gameId = GameId(get(row, "gameId"));
                if (gameId == null || !gameId.StartsWith(seasonPrefix, StringComparison.Ordinal)) continue;
                var minutes = Num(new string(get(row, "minutes").Where(c => char.IsDigit(c) || c == '.').ToArray()));
                if (!minutes.HasValue || minutes.Value <= 0) continue;
                var playerId = Id(get(row, "playerId"));
                if (playerId.Length == 0) continue;
                latest[gameId + "|" + playerId] = row;
            }

            var totals = new Dictionary<string, Totals>();
            foreach (var pair in latest.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var row = pair.Value;
                var playerId = Id(get(row, "playerId"));
                Totals t;
                if (!totals.TryGetValue(playerId, out t))
                {
                    t = new Totals { NbaId = playerId };
                    totals[playerId] = t;
                }

                t.Games++;
                t.Name = get(row, "playerName").Trim();
                var date = Date(get(row, "gameDate"));
                if (date.HasValue && (!t.LastGame.HasValue || date.Value >= t.LastGame.Value))
                {
                    t.LastGame = date;
                    t.Team = get(row, "teamAbbreviation").Trim();
                }

                var tracked = Num(get(row, "trackedFga"));
                var x2 = Num(get(row, "x2Makes"));
                var x3 = Num(get(row, "x3Makes"));
                var fga = Num(get(row, "boxFga"));
                var threeAtt = Num(get(row, "box3pa"));
                var twoAtt = Num(get(row, "tracked2pa"));
                var trackedThree = Num(get(row, "tracked3pa"));
                var shootingOk = tracked.HasValue && x2.HasValue && x3.HasValue && fga.HasValue && threeAtt.HasValue && twoAtt.HasValue && trackedThree.HasValue
                    && tracked.Value >= 0 && tracked.Value <= fga.Value
                    && twoAtt.Value >= 0 && twoAtt.Value <= fga.Value - threeAtt.Value
                    && trackedThree.Value >= 0 && trackedThree.Value <= threeAtt.Value
                    && Math.Abs(tracked.Value - twoAtt.Value - trackedThree.Value) < 0.00001
                    && x2.Value >= 0 && x2.Value <= twoAtt.Value
                    && x3.Value >= 0 && x3.Value <= trackedThree.Value
                    && (tracked.Value > 0 || fga.Value == 0);

                if (shootingOk)
                {
                    t.Threes.Add(x3);
                    t.ExpectedFgm += x2.Value + x3.Value;
                    t.TrackedFga += tracked.Value;
                    t.CoveredBoxFga += fga.Value;
                    t.ExpectedThreeMakes += x3.Value;
                    t.TrackedThreeAttempts += trackedThree.Value;
                    if (tracked.Value < fga.Value) t.PartialShootingGames++;
                    t.Points.Add(Num(get(row, "fullyExpectedPoints")));
                }
                else if (tracked.HasValue)
                {
                    t.InvalidShootingGames++;
                }

                t.Rebounds.Add(Num(get(row, "xRebounds")));
                t.Assists.Add(Num(get(row, "xAssists")));
                t.Steals.Add(Num(get(row, "xSteals")));
                t.Blocks.Add(Num(get(row, "xBlocks")));
                t.Turnovers.Add(Num(get(row, "xTurnovers")));

                var xFtm = Num(get(row, "xFtm"));
                var fta = Num(get(row, "boxFta"));
                if (xFtm.HasValue && fta.HasValue)
                {
                    t.ExpectedFtm += xFtm.Value;
                    t.Fta += fta.Value;
                    t.FtGames++;
                }
            }

            result.Players = totals.Values
                .Select(t => new XStatsPlayer
                {
                    NbaId = t.NbaId,
                    Name = t.Name,
                    Team = t.Team,
                    GamesPlayed = t.Games,
                    LastGame = t.LastGame,
                    Points = t.Points.Average,
                    Threes = t.Threes.Average,
                    Rebounds = t.Rebounds.Average,
                    Assists = t.Assists.Average,
                    Steals = t.Steals.Average,
                    Blocks = t.Blocks.Average,
                    Turnovers = t.Turnovers.Average,
                    FgPct = t.TrackedFga > 0 ? t.ExpectedFgm / t.TrackedFga : (double?)null,
                    FtPct = t.Fta > 0 ? t.ExpectedFtm / t.Fta : (double?)null,
                    ThreePct = t.TrackedThreeAttempts > 0 ? t.ExpectedThreeMakes / t.TrackedThreeAttempts : (double?)null,
                    ShotCoverage = t.CoveredBoxFga > 0 ? t.TrackedFga / t.CoveredBoxFga : (t.Threes.Count > 0 ? 1.0 : (double?)null),
                    Status = Status(t)
                })
                .OrderByDescending(p => p.Points ?? 0)
                .ToList();

            result.Success = true;
            return result;
        }

        private static string Status(Totals t)
        {
            var categoryGames = new[] { t.Points.Count, t.Threes.Count, t.Rebounds.Count, t.Assists.Count, t.Steals.Count, t.Blocks.Count, t.FtGames };
            if (categoryGames.Any(g => g < 1))
                return "Unranked: missing category / threshold";
            if (categoryGames.Any(g => g < 5))
                return "Provisional: small sample";
            if (categoryGames.Any(g => g < t.Games) || t.PartialShootingGames > 0 || t.InvalidShootingGames > 0)
                return "Provisional: incomplete coverage";
            return "Covered sample";
        }

        private static string GameId(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            double d;
            if (text.All(char.IsDigit)) return text.PadLeft(10, '0');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return ((long)Math.Round(d)).ToString("0000000000", CultureInfo.InvariantCulture);
            return text;
        }

        private static string Id(string text)
        {
            text = (text ?? "").Trim();
            double d;
            if (text.Length > 0 && !text.All(char.IsDigit) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
            return text;
        }

        private static double? Num(string text)
        {
            double d;
            return double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : (double?)null;
        }

        private static DateTime? Date(string text)
        {
            DateTime d;
            return DateTime.TryParseExact((text ?? "").Trim(), new[] { "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : (DateTime?)null;
        }

        private static List<List<string>> ParseCsv(string text)
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
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
            return rows;
        }

        private class Totals
        {
            public string NbaId { get; set; } = "";
            public string Name { get; set; } = "";
            public string Team { get; set; } = "";
            public int Games { get; set; }
            public DateTime? LastGame { get; set; }
            public Stat Points { get; } = new Stat();
            public Stat Threes { get; } = new Stat();
            public Stat Rebounds { get; } = new Stat();
            public Stat Assists { get; } = new Stat();
            public Stat Steals { get; } = new Stat();
            public Stat Blocks { get; } = new Stat();
            public Stat Turnovers { get; } = new Stat();
            public int FtGames { get; set; }
            public int PartialShootingGames { get; set; }
            public int InvalidShootingGames { get; set; }
            public double CoveredBoxFga { get; set; }
            public double ExpectedFgm { get; set; }
            public double TrackedFga { get; set; }
            public double ExpectedThreeMakes { get; set; }
            public double TrackedThreeAttempts { get; set; }
            public double ExpectedFtm { get; set; }
            public double Fta { get; set; }
        }

        private class Stat
        {
            private double _sum;
            private int _count;
            public void Add(double? v) { if (v.HasValue) { _sum += v.Value; _count++; } }
            public int Count { get { return _count; } }
            public double? Average { get { return _count > 0 ? _sum / _count : (double?)null; } }
        }
    }
}
