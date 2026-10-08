using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PwcApi.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PwcApi.Services
{
    public class WhapiResult
    {
        public bool Success { get; init; }
        public int StatusCode { get; init; }
        public string? Error { get; init; }

        /// <summary>401/403 = token invalid or WhatsApp channel disconnected → stop the whole job.</summary>
        public bool IsAuthError => StatusCode == 401 || StatusCode == 403;

        public static WhapiResult Ok(int status) => new WhapiResult { Success = true, StatusCode = status };
        public static WhapiResult Fail(int status, string? error) => new WhapiResult { Success = false, StatusCode = status, Error = error };
    }

    /// <summary>
    /// Thin wrapper over the Whapi.Cloud REST API.
    /// Docs: POST https://gate.whapi.cloud/messages/text  { to, body }
    ///       POST https://gate.whapi.cloud/messages/image { to, media, caption }
    /// "to" = international number digits only (919876543210). "media" = public URL or base64 data URI.
    /// Registered in Program.cs with AddHttpClient&lt;WhapiClient&gt; (BaseAddress = https://gate.whapi.cloud/).
    /// </summary>
    public class WhapiClient
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<WhapiClient> _logger;

        public WhapiClient(HttpClient http, IConfiguration config, ILogger<WhapiClient> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Token priority: the coach/counselor's own ResourceMasters.WhapiToken, otherwise the company-wide
        /// token from configuration (appsettings "Whapi:DefaultToken" or Railway env var Whapi__DefaultToken).
        /// </summary>
        public string? ResolveToken(ResourceMaster? person)
        {
            if (!string.IsNullOrWhiteSpace(person?.WhapiToken)) return person!.WhapiToken!.Trim();
            var fallback = _config["Whapi:DefaultToken"];
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
        }

        public Task<WhapiResult> SendTextAsync(string token, string to, string body, CancellationToken ct = default)
        {
            var payload = new Dictionary<string, object?> { ["to"] = to, ["body"] = body };
            return PostAsync(token, "messages/text", payload, ct);
        }

        /// <param name="mediaType">image | video | document</param>
        public Task<WhapiResult> SendMediaAsync(string token, string mediaType, string to, string media,
                                                string? caption, string? fileName = null, CancellationToken ct = default)
        {
            var payload = new Dictionary<string, object?> { ["to"] = to, ["media"] = media };
            if (!string.IsNullOrWhiteSpace(caption)) payload["caption"] = caption;
            if (!string.IsNullOrWhiteSpace(fileName)) payload["filename"] = fileName;
            return PostAsync(token, $"messages/{mediaType.ToLowerInvariant()}", payload, ct);
        }

        private async Task<WhapiResult> PostAsync(string token, string path, Dictionary<string, object?> payload, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request, ct);
                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode) return WhapiResult.Ok(status);

                var body = await response.Content.ReadAsStringAsync(ct);
                if (body.Length > 500) body = body.Substring(0, 500);
                _logger.LogWarning("Whapi {Path} failed: {Status} {Body}", path, status, body);
                return WhapiResult.Fail(status, $"WhatsApp API {status}: {body}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Whapi {Path} request error", path);
                return WhapiResult.Fail(0, "Network error talking to WhatsApp API: " + ex.Message);
            }
        }

        /// <summary>
        /// Turns raw base64 (or an existing data URI) into a data URI with the right mime type.
        /// Returns null if the string isn't valid base64.
        /// </summary>
        public static string? ToDataUri(string? base64OrDataUri, string fallbackMime = "image/jpeg")
        {
            if (string.IsNullOrWhiteSpace(base64OrDataUri)) return null;
            var s = base64OrDataUri.Trim();
            if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && s.Contains(";base64,")) return s;

            var comma = s.IndexOf(',');
            if (comma >= 0) s = s.Substring(comma + 1);

            byte[] bytes;
            try { bytes = Convert.FromBase64String(s); }
            catch (FormatException) { return null; }

            var mime = fallbackMime;
            if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8) mime = "image/jpeg";
            else if (bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) mime = "image/png";
            else if (bytes.Length > 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46) mime = "application/pdf";

            return $"data:{mime};base64,{s}";
        }
    }
}
