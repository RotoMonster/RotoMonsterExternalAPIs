using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    /// <summary>
    /// One player as CBS lists them on a league's stats page.
    ///
    /// The point of this is the id. Rosters and draft results come back as CBS
    /// player ids and nothing else, so without a mapping from those ids to real
    /// players an import matches nothing. This page is where that mapping comes
    /// from, and it carries the name, positions and pro team alongside so the
    /// match can be checked rather than trusted.
    /// </summary>
    public class CBSPlayer
    {
        public string PlayerId { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// CBS's position codes. More than one where a player qualifies at
        /// several, which is the difference between Ken's Multi and Single
        /// leagues.
        /// </summary>
        public List<string> Positions { get; set; } = new List<string>();

        /// <summary>The real team, e.g. DEN. Not the fantasy team.</summary>
        public string ProTeam { get; set; }
    }

    /// <summary>
    /// Reads the whole player pool off a league's stats page.
    ///
    /// The url is /stats/stats-main?print_rows=9999 on any league the user can
    /// see. print_rows is what turns the paged table into one long list - a
    /// little over 1200 rows for basketball, in a single request.
    ///
    /// The ids are CBS's own and are the same in every league, so this only has
    /// to be read once per sport rather than once per league.
    /// </summary>
    public static class CBSPlayerListParser
    {
        /// <summary>
        /// The row shape, once the escaping is undone:
        ///
        ///   &lt;a class='playerLink' aria-label='...' href='/players/playerpage/2135542'&gt;Nikola Jokic&lt;/a&gt;
        ///   &lt;span class="playerPositionAndTeam"&gt;C &amp;#149; DEN&lt;/span&gt;
        ///
        /// Anchored on playerLink and playerPositionAndTeam rather than on cell
        /// positions, so the surrounding columns can change without breaking
        /// this.
        /// </summary>
        private static readonly Regex RowRx = new Regex(
            "playerLink'[^>]*href='/players/playerpage/(\\d+)'>([^<]*)</a>" +
            "\\s*<span class=\"playerPositionAndTeam\">([^<]*)<",
            RegexOptions.IgnoreCase);

        private static readonly char[] BulletChars = { '\u2022', '\u0095', '\u00b7' };

        public static List<CBSPlayer> Parse(string html)
        {
            var players = new List<CBSPlayer>();

            if (string.IsNullOrEmpty(html))
                return players;

            // The table arrives inside a JavaScript string, so its quotes and
            // newlines are escaped. Undoing that first means the markup can be
            // matched normally.
            var clean = html
                .Replace("\\'", "'")
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n");

            // CBS repeats a player in more than one table on some pages, and
            // the id is the thing that has to be unique.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in RowRx.Matches(clean))
            {
                var id = match.Groups[1].Value;
                if (id.Length == 0 || !seen.Add(id)) continue;

                var player = new CBSPlayer
                {
                    PlayerId = id,
                    Name = Decode(match.Groups[2].Value)
                };

                // "PG,SG &#149; MIN" - positions, a bullet, then the pro team.
                //
                // CBS sends &#149;, which decodes to U+0095 rather than the
                // bullet proper. Other pages use a real bullet or a middle
                // dot, so all three are accepted - looking for only one of
                // them silently leaves the team stuck on the end of the
                // positions.
                var detail = Decode(match.Groups[3].Value);
                var bullet = detail.IndexOfAny(BulletChars);

                var positions = bullet >= 0 ? detail.Substring(0, bullet) : detail;
                if (bullet >= 0)
                    player.ProTeam = detail.Substring(bullet + 1).Trim();

                foreach (var position in positions.Split(','))
                {
                    var trimmed = position.Trim();
                    if (trimmed.Length > 0)
                        player.Positions.Add(trimmed);
                }

                players.Add(player);
            }

            return players;
        }

        private static string Decode(string value)
        {
            var decoded = System.Net.WebUtility.HtmlDecode(value ?? "");
            return Regex.Replace(decoded, "\\s+", " ").Trim();
        }
    }
}
