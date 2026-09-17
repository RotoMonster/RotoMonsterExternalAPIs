using System.Collections.Generic;
using RotoMonsterExternalAPIs.Client.Models.Providers;

namespace RotoMonsterExternalAPIs.Client.Models.Results
{
    public class GetSportsDataTeamsResult : BaseResult
    {
        public List<SportsDataTeam> Teams { get; set; } = new List<SportsDataTeam>();
        public bool NotModified { get; set; }

        /// <summary>
        /// Their own timestamp for when this feed last changed. Store it and
        /// pass it back as previousLastUpdated next time, and an unchanged
        /// feed comes back NotModified with nothing to write.
        /// </summary>
        public string LastUpdatedOn { get; set; }
    }
}
