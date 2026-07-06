using RotoMonsterExternalAPIs.Client.Models.Requests;
using RotoMonsterExternalAPIs.Client.Models.Results;
using System;
using System.Threading.Tasks;

namespace RotoMonsterExternalAPIs.Client.Services
{
    public class ExternalAPIService : IExternalAPIService
    {
        private readonly string _apiUrl;

        public ExternalAPIService(string apiUrl)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
                throw new ArgumentException("Base URL cannot be empty", nameof(apiUrl));

            _apiUrl = apiUrl.TrimEnd('/') + "/";
        }

        public async Task<GetGameWeatherResult> GetGameWeatherAsync(GetGameWeatherRequest request)
        {
            try
            {
                var weatherService = new WeatherService();
                return await weatherService.GetGameWeatherV2Async(request.TeamName, request.EasternDateTime, request.IsRetractableRoof).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<GetGameWeatherResult>(ex.Message);
            }
        }
    }
}