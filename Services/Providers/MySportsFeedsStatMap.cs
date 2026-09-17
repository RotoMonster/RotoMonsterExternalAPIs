using System.Collections.Generic;
using RotoMonsterExternalAPIs.Client.Models.Providers;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    /// <summary>
    /// Their stat names to ours, per sport.
    ///
    /// This is the only sport-specific part of the provider. Everything else
    /// (urls, auth, season format, feed names) is identical across the four
    /// leagues, so adding MLB or NHL later is a new entry here rather than a
    /// new provider.
    ///
    /// Keys on the left are theirs exactly as the feed spells them. Values on
    /// the right are our column names, so the caller can write them straight
    /// into the table without knowing anything about MySportsFeeds.
    /// </summary>
    internal static class MySportsFeedsStatMap
    {
        internal static Dictionary<string, string> For(SportsDataSport sport)
        {
            if (sport == SportsDataSport.NFL) return Nfl;
            return new Dictionary<string, string>();
        }

        private static readonly Dictionary<string, string> Nfl =
            new Dictionary<string, string>
            {
                { "passAttempts",    "PassAttempts" },
                { "passCompletions", "PassCompletions" },
                { "passYards",       "PassYards" },
                { "passTD",          "PassTD" },
                { "passInt",         "PassInt" },
                { "passSacks",       "PassSacks" },
                { "passSackY",       "PassSackYards" },

                { "rushAttempts",    "RushAttempts" },
                { "rushYards",       "RushYards" },
                { "rushTD",          "RushTD" },
                { "rushFumbles",     "RushFumbles" },

                { "targets",         "RecTargets" },
                { "receptions",      "RecReceptions" },
                { "recYards",        "RecYards" },
                { "recTD",           "RecTD" },
                { "recFumbles",      "RecFumbles" },

                { "fumLost",         "FumblesLost" },

                { "fgAtt",           "FieldGoals" },
                { "fgMade",          "FieldGoalsMade" },
                { "fgBlk",           "FieldGoalsBlocked" },
                { "fgLng",           "FieldGoalsLongest" },
                { "fgMade1_19",      "FieldGoals0to19" },
                { "fgMade20_29",     "FieldGoals20to29" },
                { "fgMade30_39",     "FieldGoals30to39" },
                { "fgMade40_49",     "FieldGoals40to49" },
                { "fgMade50Plus",    "FieldGoals50" },

                { "xpAtt",           "ExtraPointsAttempts" },
                { "xpMade",          "ExtraPointsMade" },
                { "xpBlk",           "ExtraPointsBlocked" },

                { "gamesStarted",    "Started" },

                { "offenseSnaps",      "OffenseSnaps" },
                { "defenseSnaps",      "DefenseSnaps" },
                { "specialTeamSnaps",  "SpecialTeamSnaps" },

                { "tackleSolo",            "TacklesSolo" },
                { "tackleAst",             "TacklesAssisted" },
                { "tackleTotal",           "TacklesTotal" },
                { "tacklesForLoss",        "TacklesForLoss" },
                { "sacks",                 "Sacks" },
                { "sackYds",               "SackYards" },
                { "interceptions",         "Interceptions" },
                { "intYds",                "InterceptionYards" },
                { "intTD",                 "InterceptionTouchdowns" },
                { "passesDefended",        "PassesDefended" },
                { "safeties",              "Safeties" },
                { "fumForced",             "FumblesForced" },
                { "fumOppRec",             "FumbleRecoveries" },
                { "fumTD",                 "FumbleTouchdowns" },

                { "krRet",  "KickReturns" },
                { "krYds",  "KickReturnYards" },
                { "krTD",   "KickReturnTouchdowns" },
                { "prRet",  "PuntReturns" },
                { "prYds",  "PuntReturnYards" },
                { "prTD",   "PuntReturnTouchdowns" }
            };

        /// <summary>
        /// Ours that are the sum of two of theirs. Kept separate so the plain
        /// mapping above stays a straight lookup.
        /// </summary>
        internal static readonly Dictionary<string, string[]> NflDerived =
            new Dictionary<string, string[]>
            {
                { "ReturnReturns", new[] { "KickReturns", "PuntReturns" } },
                { "ReturnYards",   new[] { "KickReturnYards", "PuntReturnYards" } },
                { "ReturnTD",      new[] { "KickReturnTouchdowns", "PuntReturnTouchdowns" } },
                { "Fumbles",       new[] { "RushFumbles", "RecFumbles" } }
            };
    }
}
