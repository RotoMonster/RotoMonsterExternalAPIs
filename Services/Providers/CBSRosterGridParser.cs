using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RotoMonsterExternalAPIs.Client.Models.Providers;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    /// <summary>
    /// Reads a league's rosters off the roster grid page.
    ///
    /// The grid is one table: a header of position columns, then a row per
    /// fantasy team. The first cell names the team and links to it, and each
    /// cell after that holds the players filling that position, wrapped in
    /// playerTeam-list-item divs.
    ///
    /// Two things worth knowing. Player names are ABBREVIATED, "D Garland"
    /// rather than Darius Garland, so they are carried only for looking at
    /// when something fails to match - the CBS id is what the caller uses. And
    /// the markup here uses unquoted href attributes, unlike the stats page,
    /// so the two pages need different patterns.
    /// </summary>
    internal static class CBSRosterGridParser
    {
        private static readonly Regex ScriptRx =
            new Regex("<(script|style).*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex TableRx =
            new Regex("<table[^>]*>.*?</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex RowRx =
            new Regex("<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex CellRx =
            new Regex("<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex TeamRx =
            new Regex("<a href='/teams/(\\d+)'>([^<]*)</a>", RegexOptions.IgnoreCase);

        /// <summary>
        /// A player, and the status that follows them when they have one. The
        /// href is unquoted on this page, hence no quotes in the pattern.
        /// </summary>
        private static readonly Regex PlayerRx = new Regex(
            "playerTeam-list-item'>\\s*<a[^>]*href=/players/playerpage/(\\d+)[^>]*>([^<]*)</a>" +
            "(?:\\s*<span class='statusPlayerTeam'>\\(([^)]*)\\)</span>)?",
            RegexOptions.IgnoreCase);

        private static readonly Regex TagRx = new Regex("<[^>]+>", RegexOptions.Singleline);

        public static List<ProviderTeam> Parse(string leagueId, string html, string myTeamId)
        {
            var teams = new List<ProviderTeam>();

            if (string.IsNullOrEmpty(html))
                return teams;

            // The page arrives with its markup escaped inside a JavaScript
            // string, same as the stats page.
            var clean = html
                .Replace("\\'", "'")
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n");

            clean = ScriptRx.Replace(clean, "");

            var table = FindRosterTable(clean);
            if (table == null)
                return teams;

            var rows = new List<List<string>>();
            foreach (Match row in RowRx.Matches(table))
            {
                var cells = new List<string>();
                foreach (Match cell in CellRx.Matches(row.Groups[1].Value))
                    cells.Add(cell.Groups[1].Value);

                if (cells.Count > 0)
                    rows.Add(cells);
            }

            if (rows.Count < 2)
                return teams;

            // The header names the position columns, and the first is the team
            // rather than a position.
            var positions = new List<string>();
            foreach (var cell in rows[0])
                positions.Add(Text(cell));

            for (var r = 1; r < rows.Count; r++)
            {
                var cells = rows[r];
                if (cells.Count < 2) continue;

                var teamMatch = TeamRx.Match(cells[0]);
                if (!teamMatch.Success) continue;

                var teamId = teamMatch.Groups[1].Value;

                var team = new ProviderTeam
                {
                    LeagueId = leagueId,
                    TeamId = teamId,
                    Title = Text(teamMatch.Groups[2].Value),
                    IsMyTeam = !string.IsNullOrEmpty(myTeamId)
                        && string.Equals(teamId, myTeamId, StringComparison.OrdinalIgnoreCase)
                };

                for (var c = 1; c < cells.Count; c++)
                {
                    var position = c < positions.Count ? positions[c] : "";

                    foreach (Match player in PlayerRx.Matches(cells[c]))
                    {
                        // Reserve is the only status seen so far. Anything else
                        // still counts as not active rather than being guessed
                        // at, since a status at all means they are not starting.
                        var status = player.Groups[3].Value;
                        var hasStatus = player.Groups[3].Success && status.Length > 0;

                        team.Players.Add(new ProviderRosterPlayer
                        {
                            PlayerId = player.Groups[1].Value,

                            // Abbreviated on this page, so it is for reading
                            // rather than matching.
                            Name = Text(player.Groups[2].Value),

                            PositionCode = position,
                            IsActive = !hasStatus,
                            IsIR = string.Equals(status, "IR", StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }

                teams.Add(team);
            }

            return teams;
        }

        /// <summary>
        /// The page carries several tables, so the roster one is found by what
        /// it contains rather than by its position.
        /// </summary>
        private static string FindRosterTable(string html)
        {
            foreach (Match table in TableRx.Matches(html))
            {
                if (table.Value.IndexOf("playerTeam-list-item", StringComparison.OrdinalIgnoreCase) >= 0)
                    return table.Value;
            }

            return null;
        }

        private static string Text(string html)
        {
            var stripped = TagRx.Replace(html ?? "", " ");
            stripped = System.Net.WebUtility.HtmlDecode(stripped);
            return Regex.Replace(stripped, "\\s+", " ").Trim();
        }
    }
}
