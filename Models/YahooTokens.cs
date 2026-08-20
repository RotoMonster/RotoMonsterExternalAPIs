using System;

namespace RotoMonsterExternalAPIs.Client.Models
{
    /// <summary>
    /// What gets stored per user. Whatever holds these owns them - this library
    /// only reads and writes them through IYahooTokenStore.
    /// </summary>
    public class YahooTokens
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
