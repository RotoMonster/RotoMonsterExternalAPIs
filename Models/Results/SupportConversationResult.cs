using System.Collections.Generic;
using RotoMonsterExternalAPIs.Client.Models.Support;

namespace RotoMonsterExternalAPIs.Client.Models.Results
{
    public class SupportConversationResult : BaseResult
    {
        public List<SupportMessage> Messages { get; set; } = new List<SupportMessage>();
    }
}
