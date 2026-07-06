using System;
using System.Collections.Generic;
using System.Text;

namespace RotoMonsterExternalAPIs.Client.Models.Requests
{
    public class GetGameWeatherRequest : BaseRequest
    {
        public string TeamName { get; set; }
        public DateTime EasternDateTime { get; set; }
        public int ApiSourceSetupId { get; set; } = 1;
        public bool IsRetractableRoof { get; set; } = false;
    }
}