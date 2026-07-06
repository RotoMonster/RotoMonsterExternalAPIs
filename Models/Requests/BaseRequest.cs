using System;
using System.Collections.Generic;
using System.Text;

namespace RotoMonsterExternalAPIs.Client.Models.Requests
{
    public abstract class BaseRequest
    {
        public bool SaveToDatabase { get; set; } = false;
    }
}
