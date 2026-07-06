using RotoMonsterExternalAPIs.Client.Models.Requests;
using RotoMonsterExternalAPIs.Client.Models.Results;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
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
                using (var client = new HttpClient())
                {
                    var url = $"{_apiUrl}api/AIResponse/GetGameWeatherV2" +
                            $"?teamName={Uri.EscapeDataString(request.TeamName)}" +
                            $"&easternDateTime={Uri.EscapeDataString(request.EasternDateTime.ToString("yyyy-MM-ddTHH:mm:ss"))}" +
                            $"&isRetractableRoof={request.IsRetractableRoof.ToString().ToLower()}" +
                            $"&apiSourceSetupId={request.ApiSourceSetupId}";

                    var httpResponse = await client.GetAsync(url).ConfigureAwait(false);
                    httpResponse.EnsureSuccessStatusCode();

                    var responseBody = await httpResponse.Content.ReadAsStringAsync();
                    var parsed = JsonSerializer.Deserialize<JsonElement>(responseBody);

                    var hourlyForecasts = new List<HourlyWeather>();
                    if (parsed.TryGetProperty("hourlyForecasts", out var hourly))
                    {
                        foreach (var h in hourly.EnumerateArray())
                        {
                            hourlyForecasts.Add(new HourlyWeather
                            {
                                Hour = DateTime.Parse(h.GetProperty("hour").GetString()),
                                Temperature = h.GetProperty("temperature").GetDouble(),
                                PercentChanceRain = h.GetProperty("percentChanceRain").GetInt32(),
                                ToWindField = h.GetProperty("toWindField").GetString(),
                                WindFieldDegrees = h.GetProperty("windFieldDegrees").GetInt32(),
                                WindSpeedLow = h.GetProperty("windSpeedLow").GetDouble(),
                                WindSpeedHigh = h.GetProperty("windSpeedHigh").GetDouble(),
                                Humidity = h.GetProperty("humidity").GetInt32()
                            });
                        }
                    }

                    return new GetGameWeatherResult
                    {
                        Success = true,
                        WindFactor = parsed.TryGetProperty("windFactor", out var wf) ? wf.GetString() : "none",
                        PostponementFactor = parsed.TryGetProperty("postponementFactor", out var pf) ? pf.GetString() : "none",
                        PostponementReason = parsed.TryGetProperty("postponementReason", out var pr) ? pr.GetString() : null,
                        DomeFactor = parsed.TryGetProperty("domeFactor", out var df) ? df.GetString() : null,
                        AvgTemp = parsed.TryGetProperty("avgTemp", out var at) ? at.GetDouble() : 0,
                        AvgRainChance = parsed.TryGetProperty("avgRainChance", out var arc) ? arc.GetDouble() : 0,
                        RainHours = parsed.TryGetProperty("rainHours", out var rh) ? rh.GetInt32() : 0,
                        HourlyForecasts = hourlyForecasts,
                        AvgToWindSpeedLow = parsed.TryGetProperty("avgToWindSpeedLow", out var awsl) ? awsl.GetDouble() : 0,
                        AvgToWindSpeedHigh = parsed.TryGetProperty("avgToWindSpeedHigh", out var awsh) ? awsh.GetDouble() : 0,
                        AvgToWindDirection = parsed.TryGetProperty("avgToWindDirection", out var awd) ? awd.GetInt32() : 0,
                        AvgToWindField = parsed.TryGetProperty("avgToWindField", out var atw) ? atw.GetString() : "",
                        AvgHumidity = parsed.TryGetProperty("avgHumidity", out var ah) ? ah.GetInt32() : 0,
                        InputTokens = parsed.TryGetProperty("totalInputTokens", out var it) ? it.GetInt32() : 0,
                        OutputTokens = parsed.TryGetProperty("totalOutputTokens", out var ot) ? ot.GetInt32() : 0,
                        Cost = parsed.TryGetProperty("totalCost", out var cost) ? cost.GetDecimal() : 0
                    };
                }
            }
            catch (Exception ex)
            {
                return BaseResult.Failure<GetGameWeatherResult>(ex.Message);
            }
        }
    }
}