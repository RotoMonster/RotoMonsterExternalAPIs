using System.Net;

namespace RotoMonsterExternalAPIs.Client.Models.Results
{
    public class YahooApiResult : BaseResult
    {
        /// <summary>
        /// The raw response body. Yahoo returns XML by default and JSON when the
        /// url carries format=json, so this stays a string and the caller parses
        /// whichever it asked for.
        /// </summary>
        public string Content { get; set; }

        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// True on a 401, which for Yahoo means the access token is no longer
        /// good. Refresh and retry, rather than treating it as a failed call.
        /// </summary>
        public bool TokenRejected { get; set; }
    }
}
