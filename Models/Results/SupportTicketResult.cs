using System;

namespace RotoMonsterExternalAPIs.Client.Models.Results
{
    public class SupportTicketResult : BaseResult
    {
        public string TicketId { get; set; }
        public string TicketNumber { get; set; }
        public string Subject { get; set; }
        public string Status { get; set; }
        public string StatusType { get; set; }
        public DateTime? CreatedTime { get; set; }
        public DateTime? ModifiedTime { get; set; }
        public DateTime? ClosedTime { get; set; }
        public string WebUrl { get; set; }

        public bool IsClosed
        {
            get { return string.Equals(StatusType, "Closed", StringComparison.OrdinalIgnoreCase); }
        }
    }
}
