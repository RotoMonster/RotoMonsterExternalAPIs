namespace RotoMonsterExternalAPIs.Client.Models.Requests
{
    public class CreateSupportTicketRequest
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Subject { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Site { get; set; }
    }
}
