using System;
using System.Collections.Generic;
using System.Text;

namespace RotoMonsterExternalAPIs.Client.Models.Results
{
    public class HourlyWeather
    {
        public DateTime Hour { get; set; }
        public double Temperature { get; set; }
        public int PercentChanceRain { get; set; }
        public string ToWindField { get; set; }
        public int WindFieldDegrees { get; set; }
        public double WindSpeedLow { get; set; }
        public double WindSpeedHigh { get; set; }
        public int Humidity { get; set; }
    }

    public class GetGameWeatherResult : BaseResult
    {
        public List<HourlyWeather> HourlyForecasts { get; set; } = new List<HourlyWeather>();
        public double AvgToWindSpeedLow { get; set; }
        public double AvgToWindSpeedHigh { get; set; }
        public int AvgToWindDirection { get; set; }
        public string AvgToWindField { get; set; }
        public int AvgHumidity { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public decimal Cost { get; set; }
        public double DurationSeconds { get; set; }
        public string WindFactor { get; set; }
        public string PostponementFactor { get; set; }
        public string PostponementReason { get; set; }  

        public string DomeFactor { get; set; }
        public double AvgTemp { get; set; }
        public double AvgRainChance { get; set; }
        public int RainHours { get; set; }
    }
}