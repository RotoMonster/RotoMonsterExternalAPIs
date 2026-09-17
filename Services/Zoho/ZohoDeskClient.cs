using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RotoMonsterExternalAPIs.Client.Models.Requests;
using RotoMonsterExternalAPIs.Client.Models.Results;
using RotoMonsterExternalAPIs.Client.Models.Support;

namespace RotoMonsterExternalAPIs.Client.Services.Zoho
{
    public class ZohoDeskClient
    {
        private readonly HttpClient _http;
        private readonly ZohoDeskOptions _options;
        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

        private string _accessToken;
        private DateTime _accessTokenExpires = DateTime.MinValue;

        public ZohoDeskClient(ZohoDeskOptions options, HttpClient httpClient = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options;
            _http = httpClient ?? new HttpClient();
        }

        public bool IsConfigured
        {
            get
            {
                return !string.IsNullOrWhiteSpace(_options.ClientId)
                       && !string.IsNullOrWhiteSpace(_options.ClientSecret)
                       && !string.IsNullOrWhiteSpace(_options.RefreshToken)
                       && !string.IsNullOrWhiteSpace(_options.OrgId)
                       && !string.IsNullOrWhiteSpace(_options.DepartmentId);
            }
        }

        public async Task<SupportTicketResult> CreateTicketAsync(CreateSupportTicketRequest request)
        {
            if (request == null) return BaseResult.Failure<SupportTicketResult>("No request.");
            if (string.IsNullOrWhiteSpace(request.Email)) return BaseResult.Failure<SupportTicketResult>("An email address is required.");
            if (string.IsNullOrWhiteSpace(request.Subject)) return BaseResult.Failure<SupportTicketResult>("A subject is required.");
            if (string.IsNullOrWhiteSpace(request.Description)) return BaseResult.Failure<SupportTicketResult>("A description is required.");

            var name = string.IsNullOrWhiteSpace(request.Name) ? request.Email.Trim() : request.Name.Trim();

            var description = new StringBuilder(Escape(request.Description.Trim()).Replace("\n", "<br>"));
            if (!string.IsNullOrWhiteSpace(request.Site) || !string.IsNullOrWhiteSpace(request.Category))
            {
                description.Append("<br><br>");
                if (!string.IsNullOrWhiteSpace(request.Site)) description.Append("Site: ").Append(Escape(request.Site)).Append("<br>");
                if (!string.IsNullOrWhiteSpace(request.Category)) description.Append("Category: ").Append(Escape(request.Category));
            }

            var body = new Dictionary<string, object>
            {
                { "subject", request.Subject.Trim() },
                { "departmentId", _options.DepartmentId },
                { "description", description.ToString() },
                { "channel", "Web" },
                { "email", request.Email.Trim() },
                { "contact", new Dictionary<string, object> { { "lastName", name }, { "email", request.Email.Trim() } } }
            };

            if (!string.IsNullOrWhiteSpace(request.Category)) body["category"] = request.Category.Trim();

            var response = await SendAsync(HttpMethod.Post, "/tickets", body).ConfigureAwait(false);
            if (!response.Success) return BaseResult.Failure<SupportTicketResult>(response.Error);

            return ReadTicket(response.Json);
        }

        public async Task<SupportTicketResult> GetTicketAsync(string ticketId)
        {
            if (string.IsNullOrWhiteSpace(ticketId)) return BaseResult.Failure<SupportTicketResult>("No ticket id.");

            var response = await SendAsync(HttpMethod.Get, "/tickets/" + Uri.EscapeDataString(ticketId), null).ConfigureAwait(false);
            if (!response.Success) return BaseResult.Failure<SupportTicketResult>(response.Error);

            return ReadTicket(response.Json);
        }

        public async Task<SupportConversationResult> GetConversationAsync(string ticketId)
        {
            if (string.IsNullOrWhiteSpace(ticketId)) return BaseResult.Failure<SupportConversationResult>("No ticket id.");

            var id = Uri.EscapeDataString(ticketId);
            var result = new SupportConversationResult { Success = true };

            var threads = await SendAsync(HttpMethod.Get, "/tickets/" + id + "/threads?limit=100", null).ConfigureAwait(false);
            if (!threads.Success) return BaseResult.Failure<SupportConversationResult>(threads.Error);

            foreach (var thread in Items(threads.Json))
            {
                var visibility = Str(thread, "visibility");
                if (!string.IsNullOrEmpty(visibility) && !string.Equals(visibility, "public", StringComparison.OrdinalIgnoreCase)) continue;

                var threadId = Str(thread, "id");
                if (string.IsNullOrEmpty(threadId)) continue;

                var detail = await SendAsync(HttpMethod.Get, "/tickets/" + id + "/threads/" + Uri.EscapeDataString(threadId), null).ConfigureAwait(false);
                if (!detail.Success) return BaseResult.Failure<SupportConversationResult>(detail.Error);

                var root = detail.Json.RootElement;
                var direction = Str(root, "direction");

                result.Messages.Add(new SupportMessage
                {
                    Id = threadId,
                    Author = AuthorName(root, "author"),
                    FromCustomer = string.Equals(direction, "in", StringComparison.OrdinalIgnoreCase),
                    Content = Str(root, "content") ?? Str(thread, "summary") ?? "",
                    IsHtml = !string.Equals(Str(root, "contentType"), "text/plain", StringComparison.OrdinalIgnoreCase),
                    CreatedTime = Date(root, "createdTime") ?? DateTime.MinValue
                });
            }

            var comments = await SendAsync(HttpMethod.Get, "/tickets/" + id + "/comments?limit=100", null).ConfigureAwait(false);
            if (!comments.Success) return BaseResult.Failure<SupportConversationResult>(comments.Error);

            foreach (var comment in Items(comments.Json))
            {
                if (!Bool(comment, "isPublic")) continue;

                var content = Str(comment, "content") ?? "";
                var fromCustomer = content.StartsWith(_options.CustomerReplyPrefix, StringComparison.Ordinal);

                result.Messages.Add(new SupportMessage
                {
                    Id = Str(comment, "id"),
                    Author = fromCustomer ? "You" : AuthorName(comment, "commenter"),
                    FromCustomer = fromCustomer,
                    Content = fromCustomer ? content.Substring(_options.CustomerReplyPrefix.Length) : content,
                    IsHtml = !string.Equals(Str(comment, "contentType"), "plainText", StringComparison.OrdinalIgnoreCase),
                    CreatedTime = Date(comment, "commentedTime") ?? DateTime.MinValue
                });
            }

            result.Messages = result.Messages.OrderBy(m => m.CreatedTime).ToList();
            return result;
        }

        public async Task<SupportTicketResult> AddCustomerReplyAsync(string ticketId, string message)
        {
            if (string.IsNullOrWhiteSpace(ticketId)) return BaseResult.Failure<SupportTicketResult>("No ticket id.");
            if (string.IsNullOrWhiteSpace(message)) return BaseResult.Failure<SupportTicketResult>("A message is required.");

            var id = Uri.EscapeDataString(ticketId);

            var comment = new Dictionary<string, object>
            {
                { "content", _options.CustomerReplyPrefix + message.Trim() },
                { "contentType", "plainText" },
                { "isPublic", true }
            };

            var added = await SendAsync(HttpMethod.Post, "/tickets/" + id + "/comments", comment).ConfigureAwait(false);
            if (!added.Success) return BaseResult.Failure<SupportTicketResult>(added.Error);

            var reopen = new Dictionary<string, object> { { "status", "Open" } };
            var updated = await SendAsync(new HttpMethod("PATCH"), "/tickets/" + id, reopen).ConfigureAwait(false);
            if (!updated.Success) return BaseResult.Failure<SupportTicketResult>(updated.Error);

            return ReadTicket(updated.Json);
        }

        private async Task<string> GetAccessTokenAsync(bool force)
        {
            await _tokenLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!force && _accessToken != null && DateTime.UtcNow < _accessTokenExpires) return _accessToken;

                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("client_id", _options.ClientId),
                    new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
                    new KeyValuePair<string, string>("refresh_token", _options.RefreshToken)
                });

                using (var response = await _http.PostAsync(_options.AccountsUrl.TrimEnd('/') + "/oauth/v2/token", form).ConfigureAwait(false))
                {
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text))
                    {
                        var token = Str(doc.RootElement, "access_token");
                        if (string.IsNullOrEmpty(token))
                            throw new InvalidOperationException("Zoho token refresh failed: " + (Str(doc.RootElement, "error") ?? text));

                        var seconds = 3600;
                        JsonElement expires;
                        if (doc.RootElement.TryGetProperty("expires_in", out expires) && expires.ValueKind == JsonValueKind.Number)
                            seconds = expires.GetInt32();

                        _accessToken = token;
                        _accessTokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 120));
                        return token;
                    }
                }
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private async Task<ApiResponse> SendAsync(HttpMethod method, string path, object body)
        {
            if (!IsConfigured) return ApiResponse.Fail("Zoho Desk is not configured.");

            for (var attempt = 0; attempt < 2; attempt++)
            {
                string token;
                try
                {
                    token = await GetAccessTokenAsync(attempt > 0).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return ApiResponse.Fail(ex.Message);
                }

                using (var request = new HttpRequestMessage(method, _options.ApiUrl.TrimEnd('/') + path))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", token);
                    request.Headers.Add("orgId", _options.OrgId);

                    if (body != null)
                        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                    HttpResponseMessage response;
                    try
                    {
                        response = await _http.SendAsync(request).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        return ApiResponse.Fail("Zoho request failed: " + ex.Message);
                    }

                    using (response)
                    {
                        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if ((int)response.StatusCode == 401 && attempt == 0) continue;

                        if (!response.IsSuccessStatusCode)
                            return ApiResponse.Fail("Zoho returned " + (int)response.StatusCode + ": " + Trim(text));

                        try
                        {
                            return ApiResponse.Ok(JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text));
                        }
                        catch (JsonException)
                        {
                            return ApiResponse.Fail("Zoho returned a response we could not read.");
                        }
                    }
                }
            }

            return ApiResponse.Fail("Zoho rejected the access token.");
        }

        private static SupportTicketResult ReadTicket(JsonDocument json)
        {
            var root = json.RootElement;
            return new SupportTicketResult
            {
                Success = true,
                TicketId = Str(root, "id"),
                TicketNumber = Str(root, "ticketNumber"),
                Subject = Str(root, "subject"),
                Status = Str(root, "status"),
                StatusType = Str(root, "statusType"),
                CreatedTime = Date(root, "createdTime"),
                ModifiedTime = Date(root, "modifiedTime"),
                ClosedTime = Date(root, "closedTime"),
                WebUrl = Str(root, "webUrl")
            };
        }

        private static IEnumerable<JsonElement> Items(JsonDocument json)
        {
            JsonElement data;
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("data", out data)
                && data.ValueKind == JsonValueKind.Array)
                return data.EnumerateArray().ToList();
            return Enumerable.Empty<JsonElement>();
        }

        private static string AuthorName(JsonElement element, string property)
        {
            JsonElement author;
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out author) || author.ValueKind != JsonValueKind.Object)
                return null;
            return Str(author, "name") ?? Str(author, "email");
        }

        private static string Str(JsonElement element, string property)
        {
            JsonElement value;
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out value)) return null;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind == JsonValueKind.Number) return value.GetRawText();
            return null;
        }

        private static bool Bool(JsonElement element, string property)
        {
            JsonElement value;
            return element.ValueKind == JsonValueKind.Object
                   && element.TryGetProperty(property, out value)
                   && value.ValueKind == JsonValueKind.True;
        }

        private static DateTime? Date(JsonElement element, string property)
        {
            var text = Str(element, property);
            if (string.IsNullOrEmpty(text)) return null;
            DateTime parsed;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed)
                ? parsed
                : (DateTime?)null;
        }

        private static string Escape(string text)
        {
            return System.Net.WebUtility.HtmlEncode(text ?? "");
        }

        private static string Trim(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= 300 ? text : text.Substring(0, 300);
        }

        private class ApiResponse
        {
            public bool Success;
            public string Error;
            public JsonDocument Json;

            public static ApiResponse Ok(JsonDocument json)
            {
                return new ApiResponse { Success = true, Json = json };
            }

            public static ApiResponse Fail(string error)
            {
                return new ApiResponse { Success = false, Error = error };
            }
        }
    }
}
