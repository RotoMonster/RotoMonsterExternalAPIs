using System.Collections.Generic;
using RotoMonsterExternalAPIs.Client.Models.Providers;

namespace RotoMonsterExternalAPIs.Client.Models.Results
{
    public class GetSportsDataPlayersResult : BaseResult
    {
        public List<SportsDataPlayer> Players { get; set; } = new List<SportsDataPlayer>();
        public bool NotModified { get; set; }

        /// <summary>
        /// Their own timestamp for when this feed last changed. Store it and
        /// pass it back as previousLastUpdated next time, and an unchanged
        /// feed comes back NotModified with nothing to write.
        /// </summary>
        public string LastUpdatedOn { get; set; }
    }
}
