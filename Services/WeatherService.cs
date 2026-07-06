using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;
using GeoTimeZone;
using TimeZoneConverter;

namespace RotoMonsterExternalAPIs.Client.Services
{
    public class StadiumCoordinates
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool IsIndoor { get; set; }
        public int CenterfieldDegrees { get; set; }
    }

    public class HourlyWeatherData
    {
        public DateTime Hour { get; set; }
        public double Temperature { get; set; }
        public int PercentChanceRain { get; set; }
        public string WindField { get; set; }
        public double WindSpeedLow { get; set; }
        public double WindSpeedHigh { get; set; }
        public int Humidity { get; set; }
        public int WindDirectionDegrees { get; set; }
        public int WindFieldDegrees { get; set; }
    }

    public class WeatherService
    {
        private static readonly Dictionary<string, StadiumCoordinates> Stadiums = new Dictionary<string, StadiumCoordinates>(StringComparer.OrdinalIgnoreCase)
        {
            { "Diamondbacks", new StadiumCoordinates { Latitude = 33.4455, Longitude = -112.0667, IsIndoor = false, CenterfieldDegrees = 0 } },
            { "Braves",       new StadiumCoordinates { Latitude = 33.8908, Longitude = -84.4678,  IsIndoor = false, CenterfieldDegrees = 157 } },
            { "Orioles",      new StadiumCoordinates { Latitude = 39.2838, Longitude = -76.6217,  IsIndoor = false, CenterfieldDegrees = 22 } },
            { "Red Sox",      new StadiumCoordinates { Latitude = 42.3467, Longitude = -71.0972,  IsIndoor = false, CenterfieldDegrees = 45 } },
            { "Cubs",         new StadiumCoordinates { Latitude = 41.9484, Longitude = -87.6553,  IsIndoor = false, CenterfieldDegrees = 45 } },
            { "White Sox",    new StadiumCoordinates { Latitude = 41.8299, Longitude = -87.6338,  IsIndoor = false, CenterfieldDegrees = 112 } },
            { "Reds",         new StadiumCoordinates { Latitude = 39.0979, Longitude = -84.5082,  IsIndoor = false, CenterfieldDegrees = 112 } },
            { "Guardians",    new StadiumCoordinates { Latitude = 41.4962, Longitude = -81.6852,  IsIndoor = false, CenterfieldDegrees = 0 } },
            { "Rockies",      new StadiumCoordinates { Latitude = 39.7559, Longitude = -104.9942, IsIndoor = false, CenterfieldDegrees = 0 } },
            { "Tigers",       new StadiumCoordinates { Latitude = 42.3390, Longitude = -83.0485,  IsIndoor = false, CenterfieldDegrees = 157 } },
            { "Astros",       new StadiumCoordinates { Latitude = 29.7573, Longitude = -95.3555,  IsIndoor = false, CenterfieldDegrees = 67 } },
            { "Royals",       new StadiumCoordinates { Latitude = 39.0517, Longitude = -94.4803,  IsIndoor = false, CenterfieldDegrees = 45 } },
            { "Angels",       new StadiumCoordinates { Latitude = 33.8003, Longitude = -117.8827, IsIndoor = false, CenterfieldDegrees = 45 } },
            { "Dodgers",      new StadiumCoordinates { Latitude = 34.0739, Longitude = -118.2400, IsIndoor = false, CenterfieldDegrees = 22 } },
            { "Marlins",      new StadiumCoordinates { Latitude = 25.7781, Longitude = -80.2197,  IsIndoor = false, CenterfieldDegrees = 112 } },
            { "Brewers",      new StadiumCoordinates { Latitude = 43.0280, Longitude = -87.9712,  IsIndoor = false, CenterfieldDegrees = 135 } },
            { "Twins",        new StadiumCoordinates { Latitude = 44.9817, Longitude = -93.2778,  IsIndoor = false, CenterfieldDegrees = 90 } },
            { "Mets",         new StadiumCoordinates { Latitude = 40.7571, Longitude = -73.8458,  IsIndoor = false, CenterfieldDegrees = 22 } },
            { "Yankees",      new StadiumCoordinates { Latitude = 40.8296, Longitude = -73.9262,  IsIndoor = false, CenterfieldDegrees = 67 } },
            { "Athletics",    new StadiumCoordinates { Latitude = 38.5802, Longitude = -121.5014, IsIndoor = false, CenterfieldDegrees = 22 } },
            { "Phillies",     new StadiumCoordinates { Latitude = 39.9061, Longitude = -75.1665,  IsIndoor = false, CenterfieldDegrees = 22 } },
            { "Pirates",      new StadiumCoordinates { Latitude = 40.4469, Longitude = -80.0057,  IsIndoor = false, CenterfieldDegrees = 112 } },
            { "Padres",       new StadiumCoordinates { Latitude = 32.7076, Longitude = -117.1570, IsIndoor = false, CenterfieldDegrees = 0 } },
            { "Giants",       new StadiumCoordinates { Latitude = 37.7786, Longitude = -122.3893, IsIndoor = false, CenterfieldDegrees = 112 } },
            { "Mariners",     new StadiumCoordinates { Latitude = 47.5914, Longitude = -122.3325, IsIndoor = false, CenterfieldDegrees = 45 } },
            { "Cardinals",    new StadiumCoordinates { Latitude = 38.6226, Longitude = -90.1928,  IsIndoor = false, CenterfieldDegrees = 45 } },
            { "Rays",         new StadiumCoordinates { Latitude = 27.7683, Longitude = -82.6534,  IsIndoor = true,  CenterfieldDegrees = 45 } },
            { "Rangers",      new StadiumCoordinates { Latitude = 32.7512, Longitude = -97.0832,  IsIndoor = false, CenterfieldDegrees = 67 } },
            { "Blue Jays",    new StadiumCoordinates { Latitude = 43.6414, Longitude = -79.3894,  IsIndoor = false, CenterfieldDegrees = 337 } },
            { "Nationals",    new StadiumCoordinates { Latitude = 38.8730, Longitude = -77.0074,  IsIndoor = false, CenterfieldDegrees = 22 } },
        };

        public StadiumCoordinates GetStadiumCoordinates(string teamName)
        {
            return Stadiums.TryGetValue(teamName, out var coords) ? coords : null;
        }

        public string DegreesToWindDirection(int degrees)
        {
            var directions = new[] { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
            var index = (int)((degrees + 11.25) / 22.5) % 16;
            return directions[index];
        }

        public string DegreesToWindField(int degrees)
        {
            var fields = new[] { "OUT CF", "OUT CFRF", "OUT RF", "OUT RFR", "RIGHT", "IN LFL", "IN LF", "IN CFLF", "IN CF", "IN CFRF", "IN RF", "IN RFR", "LEFT", "OUT LFL", "OUT LF", "OUT CFLF" };
            var index = (int)((degrees + 11.25) / 22.5) % 16;
            return fields[index];
        }

        public int ToWindDirectionDegrees(int fromDegrees)
        {
            return (fromDegrees + 180) % 360;
        }
        
        public (double AvgToWindSpeedLow, double AvgToWindSpeedHigh, int AvgToWindDirection, string AvgToWind, int AvgHumidity) CalculateWindAverages(List<HourlyWeatherData> hourlyData, int centerfieldDegrees)
        {
            var first3 = hourlyData.Take(3).ToList();
            if (first3.Count == 0)
                return (0, 0, 0, "", 0);

            // Average wind speeds
            var avgLow = first3.Average(h => h.WindSpeedLow);
            var avgHigh = first3.Average(h => h.WindSpeedHigh);

            // Vector average wind direction
            var sinSum = first3.Sum(h => Math.Sin(h.WindDirectionDegrees * Math.PI / 180.0));
            var cosSum = first3.Sum(h => Math.Cos(h.WindDirectionDegrees * Math.PI / 180.0));
            var avgDirectionRad = Math.Atan2(sinSum / first3.Count, cosSum / first3.Count);
            var avgDirectionDeg = (avgDirectionRad * 180.0 / Math.PI + 360) % 360;

            // Make relative to centerfield
            var relativeDirection = (int)((avgDirectionDeg - centerfieldDegrees + 360) % 360);

            var avgHumidity = (int)first3.Average(h => h.Humidity);
            var avgToWindField = DegreesToWindField(relativeDirection);
            return (Math.Round(avgLow, 1), Math.Round(avgHigh, 1), relativeDirection, avgToWindField, avgHumidity);
        }

        public async Task<List<HourlyWeatherData>> GetHourlyForecastAsync(double latitude, double longitude, DateTime easternDateTime, int centerfieldDegrees)
        {
            // Look up the stadium's IANA timezone from its coordinates
            var ianaTimeZone = TimeZoneLookup.GetTimeZone(latitude, longitude).Result;
            var stadiumTz = TZConvert.GetTimeZoneInfo(ianaTimeZone);

            // Convert easternDateTime to UTC, then to stadium local time
            var easternTz = TZConvert.GetTimeZoneInfo("America/New_York");
            var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(easternDateTime, easternTz);
            var localGameDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, stadiumTz);

            // Build date range — if 8-hour window crosses midnight locally, we need both dates
            var startDate = localGameDateTime.Date;
            var endDate = localGameDateTime.AddHours(8).Date;

            using (var client = new HttpClient())
            {
                var url = $"https://api.open-meteo.com/v1/forecast" +
                          $"?latitude={latitude}&longitude={longitude}" +
                          $"&hourly=temperature_2m,precipitation_probability,windspeed_10m,windgusts_10m,winddirection_10m,relativehumidity_2m" +
                          $"&temperature_unit=fahrenheit&windspeed_unit=mph" +
                          $"&timezone={Uri.EscapeDataString(ianaTimeZone)}" +
                          $"&start_date={startDate:yyyy-MM-dd}&end_date={endDate:yyyy-MM-dd}";

                var response = await client.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<JsonElement>(body);

                var hourly = parsed.GetProperty("hourly");
                var times = hourly.GetProperty("time");
                var temps = hourly.GetProperty("temperature_2m");
                var precip = hourly.GetProperty("precipitation_probability");
                var windSpeed = hourly.GetProperty("windspeed_10m");
                var windGusts = hourly.GetProperty("windgusts_10m");
                var windDir = hourly.GetProperty("winddirection_10m");
                var humidity = hourly.GetProperty("relativehumidity_2m");

                var results = new List<HourlyWeatherData>();
                var gameHour = localGameDateTime;

                for (int i = 0; i < times.GetArrayLength(); i++)
                {
                    var time = DateTime.Parse(times[i].GetString());
                    if (time >= gameHour && time < gameHour.AddHours(8))
                    {
                        results.Add(new HourlyWeatherData
                        {
                            Hour = time,
                            Temperature = temps[i].GetDouble(),
                            PercentChanceRain = precip[i].GetInt32(),
                            WindDirectionDegrees = ToWindDirectionDegrees(windDir[i].GetInt32()),
                            WindField = DegreesToWindField(ToWindDirectionDegrees(windDir[i].GetInt32())),
                            WindFieldDegrees = (int)((ToWindDirectionDegrees(windDir[i].GetInt32()) - centerfieldDegrees + 360) % 360),
                            WindSpeedLow = windSpeed[i].GetDouble(),
                            WindSpeedHigh = windGusts[i].GetDouble(),
                            Humidity = humidity[i].GetInt32()
                        });
                    }
                }

                return results;
            }
        }

        public async Task<RotoMonsterExternalAPIs.Client.Models.Results.GetGameWeatherResult> GetGameWeatherV2Async(string teamName, DateTime easternDateTime, bool isRetractableRoof = false)
        {
            var coords = GetStadiumCoordinates(teamName);
            if (coords == null)
                return RotoMonsterExternalAPIs.Client.Models.Results.BaseResult.Failure<RotoMonsterExternalAPIs.Client.Models.Results.GetGameWeatherResult>($"Stadium not found for team: {teamName}");

            var hourlyData = await GetHourlyForecastAsync(coords.Latitude, coords.Longitude, easternDateTime, coords.CenterfieldDegrees).ConfigureAwait(false);
            if (hourlyData == null || hourlyData.Count == 0)
                return RotoMonsterExternalAPIs.Client.Models.Results.BaseResult.Failure<RotoMonsterExternalAPIs.Client.Models.Results.GetGameWeatherResult>("Could not retrieve weather data");

            var hourlyForecasts = hourlyData.Select(h => new RotoMonsterExternalAPIs.Client.Models.Results.HourlyWeather
            {
                Hour = h.Hour,
                Temperature = h.Temperature,
                PercentChanceRain = h.PercentChanceRain,
                ToWindField = h.WindField,
                WindFieldDegrees = h.WindFieldDegrees,
                WindSpeedLow = h.WindSpeedLow,
                WindSpeedHigh = h.WindSpeedHigh,
                Humidity = h.Humidity
            }).ToList();

            var (avgToWindSpeedLow, avgToWindSpeedHigh, avgToWindDirection, avgToWindField, avgHumidity) = CalculateWindAverages(hourlyData, coords.CenterfieldDegrees);
            var avgTemp = hourlyData.Take(3).Average(h => h.Temperature);
            var avgRainChance = hourlyData.Take(3).Average(h => h.PercentChanceRain);
            var rainHours = hourlyData.Count(h => h.PercentChanceRain >= 30);

            // Step 2: If retractable, calculate dome factor from temp, humidity, and rain
            string domeFactor = null;
            if (isRetractableRoof)
            {
                var avgRain3Hours_dome = hourlyData.Take(3).Average(h => h.PercentChanceRain);

                if (avgTemp > 85 || avgHumidity > 70 || avgRain3Hours_dome >= 50)
                    domeFactor = "high";
                else if (avgRain3Hours_dome >= 25)
                    domeFactor = "medium";
                else
                    domeFactor = "low";

                if (domeFactor == "high")
                {
                    return new RotoMonsterExternalAPIs.Client.Models.Results.GetGameWeatherResult
                    {
                        Success = true,
                        DomeFactor = domeFactor,
                        WindFactor = "none",
                        PostponementFactor = "none",
                        PostponementReason = null,
                        HourlyForecasts = new List<RotoMonsterExternalAPIs.Client.Models.Results.HourlyWeather>(),
                        AvgTemp = 0,
                        AvgHumidity = 0
                    };
                }
            }

            // Step 3: Calculate wind factor from avg wind speed
            var avgWindSpeed = (avgToWindSpeedLow + avgToWindSpeedHigh) / 2;
            string windFactor;
            if (avgWindSpeed >= 21) windFactor = "high";
            else if (avgWindSpeed >= 11) windFactor = "medium";
            else windFactor = "low";

            // Step 4: Calculate postponement factor from avg rain % over first 3 hours
            var first3Hours = hourlyData.Take(3).ToList();
            var avgRain3Hours = first3Hours.Any() ? first3Hours.Average(h => h.PercentChanceRain) : 0;
            string postponementFactor;
            if (avgRain3Hours >= 75) postponementFactor = "high";
            else if (avgRain3Hours >= 50) postponementFactor = "medium";
            else if (avgRain3Hours >= 10) postponementFactor = "low";
            else postponementFactor = "none";

            return new RotoMonsterExternalAPIs.Client.Models.Results.GetGameWeatherResult
            {
                Success = true,
                DomeFactor = domeFactor,
                WindFactor = windFactor,
                PostponementFactor = postponementFactor,
                PostponementReason = null,
                AvgTemp = Math.Round(avgTemp, 1),
                AvgHumidity = avgHumidity,
                AvgRainChance = Math.Round(avgRainChance, 1),
                RainHours = rainHours,
                AvgToWindSpeedLow = avgToWindSpeedLow,
                AvgToWindSpeedHigh = avgToWindSpeedHigh,
                AvgToWindDirection = avgToWindDirection,
                AvgToWindField = avgToWindField,
                HourlyForecasts = hourlyForecasts,
                InputTokens = 0,
                OutputTokens = 0,
                Cost = 0
            };
        }
    }
}