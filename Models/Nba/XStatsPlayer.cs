using System;
using System.Collections.Generic;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Models.Nba
{
    public class XStatsPlayer
    {
        public string NbaId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Team { get; set; } = "";
        public int GamesPlayed { get; set; }
        public DateTime? LastGame { get; set; }
        public double? Points { get; set; }
        public double? Threes { get; set; }
        public double? Rebounds { get; set; }
        public double? Assists { get; set; }
        public double? Steals { get; set; }
        public double? Blocks { get; set; }
        public double? Turnovers { get; set; }
        public double? FgPct { get; set; }
        public double? FtPct { get; set; }
        public double? ThreePct { get; set; }
        public double? ShotCoverage { get; set; }
        public string Status { get; set; } = "";

        public override string ToString()
        {
            return $"{Name} ({Team}) GP {GamesPlayed}: PTS {F(Points)} 3PM {F(Threes)} REB {F(Rebounds)} AST {F(Assists)} STL {F(Steals)} BLK {F(Blocks)} TO {F(Turnovers)} FG% {P(FgPct)} FT% {P(FtPct)} 3P% {P(ThreePct)} Coverage {P(ShotCoverage)} {Status}";
        }

        private static string F(double? v) { return v.HasValue ? v.Value.ToString("0.00") : "-"; }
        private static string P(double? v) { return v.HasValue ? v.Value.ToString("0.000") : "-"; }
    }

    public class GetXStatsResult : BaseResult
    {
        public string Url { get; set; }
        public List<XStatsPlayer> Players { get; set; } = new List<XStatsPlayer>();
    }
}
