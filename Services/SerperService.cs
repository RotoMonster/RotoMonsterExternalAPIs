using RotoMonsterExternalAPIs.Client.Models.Results;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Net.Http;
using System;

namespace RotoMonsterExternalAPIs.Client.Services
{
    public class SerperService
    {
        private readonly string _apiKey;

        public SerperService(string apiKey)
        {
            _apiKey = apiKey;
        }

        private async Task<JsonElement?> SearchAsync(string query)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("X-API-KEY", _apiKey);

                var requestBody = new { q = query };
                var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://google.serper.dev/search", jsonContent);
                if (!response.IsSuccessStatusCode) return null;

                var responseBody = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<JsonElement>(responseBody);
            }
        }

        public async Task<string> SearchBaseballReferenceUrlAsync(string playerName)
        {
            var result = await SearchAsync($"{playerName} baseball reference minor league stats site:baseball-reference.com");
            if (result == null) return null;

            if (result.Value.TryGetProperty("organic", out var organic))
            {
                foreach (var item in organic.EnumerateArray())
                {
                    if (item.TryGetProperty("link", out var link))
                    {
                        var url = link.GetString();
                        if (url != null && url.Contains("/register/player.fcgi"))
                            return url;
                    }
                }
            }

            return null;
        }

        public async Task<SerperPlayerInfo> GetMLBPlayerInfoAsync(string playerName)
        {
            var result = await SearchAsync($"{playerName} MLB player full name position");
            if (result == null) return null;

            var info = new SerperPlayerInfo();

            var nameParts = playerName.Trim().Split(' ');
            if (nameParts.Length >= 2)
            {
                info.RealFirstName = nameParts[0];
                info.UsedFirstName = nameParts[0];
                info.LastName = nameParts[nameParts.Length - 1];
            }

            if (result.Value.TryGetProperty("answerBox", out var answerBox))
            {
                if (answerBox.TryGetProperty("snippet", out var snippet))
                {
                    var text = snippet.GetString() ?? "";
                    var nameMatch = Regex.Match(text, @"^([\p{L}]+(?:\s[\p{L}]+)+)\s*[\(\;]");
                    if (nameMatch.Success)
                    {
                        var fullName = nameMatch.Groups[1].Value.Trim();
                        var parts = fullName.Split(' ');
                        if (parts.Length >= 2)
                        {
                            info.LastName = parts[parts.Length - 1];
                            info.RealFirstName = parts[0];
                            info.UsedFirstName = parts.Length >= 3 ? parts[1] : parts[0];
                        }
                    }
                }
            }

            if (result.Value.TryGetProperty("organic", out var organic))
            {
                foreach (var item in organic.EnumerateArray())
                {
                    if (!item.TryGetProperty("link", out var linkEl)) continue;
                    var url = linkEl.GetString() ?? "";
                    var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";

                    if (string.IsNullOrEmpty(info.MlbId))
                    {
                        var mlbMatch = Regex.Match(url, @"mlb\.com/[^/]+/player/[^/]+-(\d+)$");
                        if (!mlbMatch.Success)
                            mlbMatch = Regex.Match(url, @"(?:mlb|milb)\.com/(?:stories/player|savant-player)/[^/]+-(\d+)");
                        if (mlbMatch.Success)
                            info.MlbId = mlbMatch.Groups[1].Value;
                    }

                    if (string.IsNullOrEmpty(info.MilbId))
                    {
                        var milbMatch = Regex.Match(url, @"milb\.com/player/[^/]+-(\d+)$");
                        if (milbMatch.Success)
                            info.MilbId = milbMatch.Groups[1].Value;
                    }

                    if (string.IsNullOrEmpty(info.PrimaryPosition) && url.Contains("baseball-reference.com/players"))
                    {
                        var posMatch = Regex.Match(snippet, @"Positions?:\s*([A-Za-z\s]+?)(?:\s*;|\s*\.|$)");
                        if (posMatch.Success)
                            info.PrimaryPosition = posMatch.Groups[1].Value.Trim();
                    }

                    if (string.IsNullOrEmpty(info.Birthdate) && url.Contains("mlb.com"))
                    {
                        var dateMatch = Regex.Match(snippet, @"Born:\s*(\d{1,2}/\d{1,2}/\d{4})");
                        if (dateMatch.Success)
                            info.Birthdate = dateMatch.Groups[1].Value;
                    }

                    if (string.IsNullOrEmpty(info.Height) || string.IsNullOrEmpty(info.WeightLbs))
                    {
                        var hwMatch = Regex.Match(snippet, @"Height/Weight:\s*([\d''\s""\\]+?)\s*/\s*(\d+)\s*lbs?");
                        if (hwMatch.Success)
                        {
                            info.Height = hwMatch.Groups[1].Value.Trim();
                            info.WeightLbs = hwMatch.Groups[2].Value.Trim();
                        }
                    }
                }
            }

            var idResult = await SearchAsync($"{playerName} MLB player site:sports.yahoo.com OR site:cbssports.com OR site:milb.com");
            if (idResult.HasValue && idResult.Value.TryGetProperty("organic", out var idOrganic))
            {
                foreach (var item in idOrganic.EnumerateArray())
                {
                    if (!item.TryGetProperty("link", out var linkEl)) continue;
                    var url = linkEl.GetString() ?? "";

                    if (string.IsNullOrEmpty(info.YahooId))
                    {
                        var yahooMatch = Regex.Match(url, @"sports\.yahoo\.com/mlb/players/(\d+)");
                        if (yahooMatch.Success)
                            info.YahooId = yahooMatch.Groups[1].Value;
                    }

                    if (string.IsNullOrEmpty(info.CbsSportsId))
                    {
                        var cbsMatch = Regex.Match(url, @"cbssports\.com/mlb/players/(\d+)");
                        if (cbsMatch.Success)
                            info.CbsSportsId = cbsMatch.Groups[1].Value;
                    }

                    if (string.IsNullOrEmpty(info.MilbId))
                    {
                        var milbMatch = Regex.Match(url, @"milb\.com/player/[^/]+-(\d+)$");
                        if (milbMatch.Success)
                            info.MilbId = milbMatch.Groups[1].Value;
                    }

                    if (!string.IsNullOrEmpty(info.YahooId) && !string.IsNullOrEmpty(info.CbsSportsId))
                        break;
                }
            }

            info.BaseballReferenceUrl = await SearchBaseballReferenceUrlAsync(playerName);

            return info;
        }
    }
}
