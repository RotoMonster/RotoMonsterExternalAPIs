using System;
using System.Collections.Generic;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Models.Nba
{
    public class NbaInjuryReportPlayer
    {
        public DateTime GameDate { get; set; }
        public string GameTime { get; set; } = "";
        public DateTime GameStartEastern { get; set; }
        public string Matchup { get; set; } = "";
        public string Team { get; set; } = "";
        public string TeamCode { get; set; } = "";
        public string ListedName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Status { get; set; } = "";
        public string Reason { get; set; } = "";
        public bool NotYetSubmitted { get; set; }

        public override string ToString()
        {
            return NotYetSubmitted
                ? $"{GameDate:M/d} {GameTime} {Matchup} {TeamCode}: NOT YET SUBMITTED"
                : $"{GameDate:M/d} {GameTime} {Matchup} {TeamCode}: {FullName} - {Status} - {Reason}";
        }
    }

    public class GetNbaInjuryReportResult : BaseResult
    {
        public string Url { get; set; }
        public DateTime? ReportTimeEastern { get; set; }
        public List<NbaInjuryReportPlayer> Players { get; set; } = new List<NbaInjuryReportPlayer>();
    }
}
