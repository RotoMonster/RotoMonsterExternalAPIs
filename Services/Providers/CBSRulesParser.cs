using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RotoMonsterExternalAPIs.Client.Models.Providers;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    /// <summary>
    /// Turns CBS's league rules page into settings.
    ///
    /// CBS has no API for this, so it is HTML. The page is server rendered and
    /// laid out as a series of small tables, nearly all of them two columns of
    /// label and value, which is far friendlier than it sounds - the parsing
    /// keys off the labels rather than table or row position, so CBS moving a
    /// section around does not break it.
    ///
    /// Split out from the provider because parsing a page and fetching one are
    /// different jobs, and this half can be tested against a saved file.
    /// </summary>
    internal static class CBSRulesParser
    {
        private static readonly Regex TableRx =
            new Regex("<table class=\"data borderTop\".*?</table>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex RowRx =
            new Regex("<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex CellRx =
            new Regex("<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex TagRx = new Regex("<[^>]+>", RegexOptions.Singleline);

        private static readonly Regex PointsRx = new Regex("-?\\d+(\\.\\d+)?");

        private static readonly Regex ScriptRx =
            new Regex("<(script|style).*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        /// <summary>
        /// The limit rows carry a label plus a min and a max. Only the ones we
        /// use are listed, so an unfamiliar row is ignored rather than guessed
        /// at.
        /// </summary>
        private static readonly string[] LimitLabels =
        {
            "Active Players", "Reserve Players", "Injured Players", "Total Players"
        };

        public static ProviderLeagueSettings Parse(string leagueId, string html)
        {
            var settings = new ProviderLeagueSettings { LeagueId = leagueId };

            if (string.IsNullOrEmpty(html))
                return settings;

            var rows = ReadRows(html);

            // Every two column row across the whole page, keyed by its label.
            // "Description" is the header on most of these tables.
            var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (row.Count != 2) continue;
                if (row[0].Length == 0) continue;
                if (string.Equals(row[0], "Description", StringComparison.OrdinalIgnoreCase)) continue;

                pairs[row[0]] = row[1];
            }

            settings.Title = Get(pairs, "League Name");
            settings.NumberOfTeams = ToInt(Get(pairs, "Teams"));

            ApplyRoster(settings, rows);
            ApplyScoring(settings, rows, pairs);
            ApplyPolicies(settings, pairs);

            return settings;
        }

        // -------------------------------------------------------------------
        // Roster
        // -------------------------------------------------------------------

        private static void ApplyRoster(ProviderLeagueSettings settings, List<List<string>> rows)
        {
            // The limits table is four columns: label, min, max, blank.
            var limits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (row.Count == 4 && LimitLabels.Contains(row[0], StringComparer.OrdinalIgnoreCase))
                    limits[row[0]] = row[2];
            }

            var active = ToInt(Get(limits, "Active Players"));
            var reserve = ToInt(Get(limits, "Reserve Players"));
            var injured = ToInt(Get(limits, "Injured Players"));
            var total = ToInt(Get(limits, "Total Players"));

            settings.IRSpots = injured;
            settings.PlayersPerTeam = total > 0 ? total : active + reserve;

            // The position table is also four columns, but its rows are short
            // position codes rather than the limit labels above.
            foreach (var row in rows)
            {
                if (row.Count != 4) continue;

                var code = row[0];
                if (code.Length == 0 || code.Length > 4) continue;
                if (LimitLabels.Contains(code, StringComparer.OrdinalIgnoreCase)) continue;
                if (string.Equals(code, "Position", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(code, "Status", StringComparison.OrdinalIgnoreCase)) continue;

                // CBS gives an active minimum and an active maximum rather than
                // a slot count, and the minimums are usually zero. The maximum
                // is the only real constraint, so that is what is carried over.
                // "No Limit" means the position is uncapped, which we cannot
                // express, so it is left out rather than invented.
                var max = row[2];
                if (max.IndexOf("No Limit", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                var count = ToInt(max);
                if (count <= 0) continue;

                settings.RosterSpots.Add(new ProviderRosterSpot { Code = code, Count = count });
            }

            if (reserve > 0)
                settings.RosterSpots.Add(new ProviderRosterSpot
                {
                    Code = "BN",
                    Count = reserve,
                    IsBench = true
                });

            if (injured > 0)
                settings.RosterSpots.Add(new ProviderRosterSpot
                {
                    Code = "IR",
                    Count = injured,
                    IsInjured = true
                });
        }

        // -------------------------------------------------------------------
        // Scoring
        // -------------------------------------------------------------------

        private static void ApplyScoring(
            ProviderLeagueSettings settings, List<List<string>> rows, Dictionary<string, string> pairs)
        {
            // Left as CBS words it, per the model. Rotisserie or Points.
            settings.ScoringSystem = Get(pairs, "Scoring System");

            // The category table is three columns: code, name, and a setting
            // that holds the points per stat in a points league and is empty
            // in a categories league. That emptiness is how the two are told
            // apart, which is why PointsPerStat stays null rather than zero.
            foreach (var row in rows)
            {
                if (row.Count != 3) continue;
                if (row[0].Length == 0) continue;
                if (string.Equals(row[0], "Stats", StringComparison.OrdinalIgnoreCase)) continue;

                var category = new ProviderCategory
                {
                    Code = row[0],
                    Name = row[1]
                };

                // CBS writes this as "2 points" or "-1 point" rather than a
                // bare number, so the number is pulled out of the wording. An
                // empty cell leaves PointsPerStat null, which is what marks a
                // categories league.
                var amount = PointsRx.Match(row[2]);
                if (amount.Success)
                {
                    double points;
                    if (double.TryParse(amount.Value, NumberStyles.Any,
                                        CultureInfo.InvariantCulture, out points))
                        category.PointsPerStat = points;
                }

                settings.Categories.Add(category);
            }
        }

        // -------------------------------------------------------------------
        // Policies
        // -------------------------------------------------------------------

        private static void ApplyPolicies(ProviderLeagueSettings settings, Dictionary<string, string> pairs)
        {
            // "Weekly scoring periods, starting on Mondays" or a daily variant.
            var period = Get(pairs, "Period Length");
            settings.LineupFrequency =
                period.IndexOf("Weekly", StringComparison.OrdinalIgnoreCase) >= 0 ? "weekly" : "daily";

            settings.StartWeekday = ToWeekday(Get(pairs, "Periods Start"));

            // "Add/drops will process immediately." is the no waivers case.
            var addDrop = Get(pairs, "Add/Drop Policy");
            if (addDrop.Length > 0)
            {
                var immediate = addDrop.IndexOf("immediately", StringComparison.OrdinalIgnoreCase) >= 0;
                settings.WaiverType = immediate ? "immediate" : "waivers";
                settings.ContinuousWaivers = !immediate;
            }

            // Not on the settings model, but it is the difference between Ken's
            // Multi and Single leagues, so it is worth not losing quietly.
            // Callers that care can read it off LeagueType.
            var eligibility = Get(pairs, "Player Eligibility");
            if (eligibility.Length > 0)
            {
                settings.LeagueType =
                    eligibility.IndexOf("multiple positions", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "multi-position"
                        : "single-position";
            }

            // "Your draft has not been set up." when there is nothing yet.
            var draftFormat = Get(pairs, "Draft Format");
            if (draftFormat.Length > 0)
            {
                settings.HasDrafted =
                    draftFormat.IndexOf("not been set up", StringComparison.OrdinalIgnoreCase) < 0;

                settings.IsAuction =
                    draftFormat.IndexOf("auction", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static List<List<string>> ReadRows(string html)
        {
            var rows = new List<List<string>>();

            // Scripts contain enough angle brackets and stray table markup to
            // confuse the row matching, so they go first.
            var clean = ScriptRx.Replace(html, "");

            foreach (Match table in TableRx.Matches(clean))
            {
                foreach (Match row in RowRx.Matches(table.Value))
                {
                    var cells = new List<string>();

                    foreach (Match cell in CellRx.Matches(row.Groups[1].Value))
                        cells.Add(Text(cell.Groups[1].Value));

                    if (cells.Count > 0)
                        rows.Add(cells);
                }
            }

            return rows;
        }

        private static string Text(string html)
        {
            var stripped = TagRx.Replace(html, "");
            stripped = System.Net.WebUtility.HtmlDecode(stripped);
            return Regex.Replace(stripped, "\\s+", " ").Trim();
        }

        private static string Get(Dictionary<string, string> pairs, string key)
        {
            string value;
            return pairs.TryGetValue(key, out value) ? value : "";
        }

        private static int ToInt(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0;
        }

        /// <summary>
        /// UserLeague.StartWeekday holds System.DayOfWeek cast to int, so the
        /// day name is turned into that rather than a CBS string.
        /// </summary>
        private static int ToWeekday(string value)
        {
            if (string.IsNullOrEmpty(value))
                return (int)DayOfWeek.Monday;

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                if (value.IndexOf(day.ToString(), StringComparison.OrdinalIgnoreCase) >= 0)
                    return (int)day;
            }

            return (int)DayOfWeek.Monday;
        }
    }
}
