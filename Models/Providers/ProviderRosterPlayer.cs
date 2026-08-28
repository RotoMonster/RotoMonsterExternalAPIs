using System;

namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// A player on a provider roster.
    ///
    /// PlayerId is the PROVIDER's id, not an RM player id. Matching it to a
    /// real player needs FantasyProviderPlayers, which lives in the database,
    /// so that stays on the caller's side. Name is carried alongside for the
    /// cases where the id does not match anything and someone has to work out
    /// who this was.
    /// </summary>
    public class ProviderRosterPlayer
    {
        public string PlayerId { get; set; }

        /// <summary>
        /// A second id the provider knows this player by, tried when the
        /// first matches nothing. Yahoo occasionally returns a player_id
        /// we have no mapping for, and the id off editorial_player_key
        /// does match. Empty for providers with one id scheme.
        /// </summary>
        public string AlternatePlayerId { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The roster slot the provider has them in, in provider codes.
        /// </summary>
        public string PositionCode { get; set; }

        public bool IsActive { get; set; }

        public bool IsIR { get; set; }

        /// <summary>
        /// For a player on waivers, the date they can be claimed. Null
        /// on a rostered player, and on a waiver player we could not
        /// find a drop for.
        /// </summary>
        public DateTime? WaiverDate { get; set; }
    }
}
