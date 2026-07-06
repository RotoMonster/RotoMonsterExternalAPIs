using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Requests;
using RotoMonsterExternalAPIs.Client.Models.Results;

namespace RotoMonsterExternalAPIs.Client.Services
{
    public interface IExternalAPIService
    {
        Task<GetGameWeatherResult> GetGameWeatherAsync(GetGameWeatherRequest request);
    }
}
