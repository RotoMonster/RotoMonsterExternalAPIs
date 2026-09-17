namespace RotoMonsterExternalAPIs.Client.Services.Zoho
{
    public class ZohoDeskOptions
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string RefreshToken { get; set; }
        public string OrgId { get; set; }
        public string DepartmentId { get; set; }
        public string AccountsUrl { get; set; } = "https://accounts.zoho.com";
        public string ApiUrl { get; set; } = "https://desk.zoho.com/api/v1";
        public string CustomerReplyPrefix { get; set; } = "[Customer] ";
    }
}
