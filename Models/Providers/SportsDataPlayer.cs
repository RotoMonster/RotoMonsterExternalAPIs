using System;

namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// A player as the provider has them. Everything needed to create one of
    /// ours from scratch, since the first match is on name and birthdate and
    /// anyone who doesn't match has to be inserted.
    /// </summary>
    public class SportsDataPlayer
    {
        public string PlayerId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Position { get; set; }
        public string TeamCode { get; set; }

        public DateTime? BirthDate { get; set; }
        public int? HeightInches { get; set; }
        public int? Weight { get; set; }
        public string College { get; set; }

        public int? RookieYear { get; set; }
        public int? DraftPickNumber { get; set; }

        /// <summary>Their roster status, e.g. ROSTER or UNDRAFTED.</summary>
        public string RosterStatus { get; set; }

        /// <summary>
        /// Null when healthy. Comes off the same roster pull rather than a
        /// separate feed, which is why injuries do not need their own call.
        /// </summary>
        public SportsDataInjury Injury { get; set; }
    }
}
