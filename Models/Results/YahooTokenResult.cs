using System;

namespace RotoMonsterExternalAPIs.Client.Models.Results
{
    public class YahooTokenResult : BaseResult
    {
        public string AccessToken { get; set; }

        /// <summary>
        /// Yahoo issues a NEW refresh token on every refresh and invalidates the
        /// old one. Storing whatever comes back is required, not optional - miss
        /// it once and the user has to authorize again from scratch.
        /// </summary>
        public string RefreshToken { get; set; }

        /// <summary>
        /// Computed from the expires_in the token response carries, rather than
        /// assuming an hour. There is no endpoint to ask Yahoo whether a token is
        /// still valid, so this is what tells us when to refresh.
        /// </summary>
        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>
        /// Identifies which Yahoo account granted access. Useful for showing who
        /// is connected, and for noticing when someone links a different account.
        /// </summary>
        public string YahooGuid { get; set; }

        /// <summary>
        /// True when Yahoo rejected the grant outright - a revoked or expired
        /// refresh token. Retrying will never fix this; the user has to go back
        /// through the authorization URL. Distinct from a network failure, where
        /// retrying is exactly the right thing to do.
        /// </summary>
        public bool NeedsReauthorization { get; set; }
    }
}
