using System;

namespace RotoMonsterExternalAPIs.Client.Models.Support
{
    public class SupportMessage
    {
        public string Id { get; set; }
        public string Author { get; set; }
        public bool FromCustomer { get; set; }
        public string Content { get; set; }
        public bool IsHtml { get; set; }
        public DateTime CreatedTime { get; set; }
    }
}
