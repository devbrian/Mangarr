using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Automation.Test.TestKit;

public enum CassetteMode
{
    Replay,            // Read cassette; throw on miss
    Record,            // Pass through to live; record to disk
    ReplayOrRecord     // Read if exists; record if not
}

/// <summary>
/// Per-test JSON cassette HttpClient handler for offline-tier replay.
/// Sentinel-PNG substitution for image content types per D-12.
/// Env-var-driven mode + cassette dir per RESEARCH §"Cassette injection mechanism".
/// </summary>
public class CassetteHandler : DelegatingHandler
{
    private readonly string _cassetteDir;
    private readonly CassetteMode _mode;
    private readonly string _sentinelPngPath;

    public CassetteHandler(string cassetteDir, CassetteMode mode, string sentinelPngPath, HttpMessageHandler innerHandler)
    {
        _cassetteDir = cassetteDir;
        _mode = mode;
        _sentinelPngPath = sentinelPngPath;
        InnerHandler = innerHandler;
        Directory.CreateDirectory(_cassetteDir);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var key = ComputeKey(request);
        var path = Path.Combine(_cassetteDir, $"{key}.json");

        if (_mode == CassetteMode.Replay)
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Cassette miss: {request.Method} {request.RequestUri}\n" +
                    $"Expected file: {path}\n" +
                    $"Run with MANGARR_TEST_CASSETTE_MODE=Record to capture.");
            }

            return await LoadFromDiskAsync(path).ConfigureAwait(false);
        }

        if (_mode == CassetteMode.ReplayOrRecord && File.Exists(path))
        {
            return await LoadFromDiskAsync(path).ConfigureAwait(false);
        }

        // Record mode (or ReplayOrRecord miss): pass through, then persist.
        var response = await base.SendAsync(request, ct).ConfigureAwait(false);

        // D-12: sentinel-PNG substitution for image content types
        if (IsImageContentType(response.Content?.Headers?.ContentType))
        {
            return await BuildSentinelResponseAsync(_sentinelPngPath, response).ConfigureAwait(false);
        }

        await WriteToDiskAsync(path, request, response).ConfigureAwait(false);
        return response;
    }

    private static string ComputeKey(HttpRequestMessage request)
    {
        var raw = $"{request.Method}|{request.RequestUri}";
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 16);
    }

    private static bool IsImageContentType(MediaTypeHeaderValue contentType)
        => contentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<HttpResponseMessage> BuildSentinelResponseAsync(string sentinelPath, HttpResponseMessage upstream)
    {
        // If no sentinel PNG configured, fall back to a 1x1 transparent PNG inline literal so tests
        // never hard-fail purely on missing sentinel asset wiring (T-18-01 mitigation — never persist
        // upstream image bytes that might be user-uploaded covers).
        byte[] bytes;
        if (!string.IsNullOrEmpty(sentinelPath) && File.Exists(sentinelPath))
        {
            bytes = await File.ReadAllBytesAsync(sentinelPath).ConfigureAwait(false);
        }
        else
        {
            // 1x1 transparent PNG (67 bytes, base64 below) — safe inline default.
            bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=");
        }

        var response = new HttpResponseMessage(upstream.StatusCode)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return response;
    }

    private static async Task<HttpResponseMessage> LoadFromDiskAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        var dto = JsonSerializer.Deserialize<CassetteDto>(json);
        var response = new HttpResponseMessage((System.Net.HttpStatusCode)dto.Response.Status)
        {
            Content = new StringContent(dto.Response.Body ?? string.Empty)
        };
        if (dto.Response.Headers != null)
        {
            foreach (var kv in dto.Response.Headers)
            {
                if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(kv.Value);
                }
                else
                {
                    response.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
        }

        return response;
    }

    private static async Task WriteToDiskAsync(string path, HttpRequestMessage request, HttpResponseMessage response)
    {
        var body = response.Content == null ? null : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // T-18-01: do NOT capture Authorization or X-Api-Key headers in the serialized DTO.
        var dto = new CassetteDto
        {
            Request = new CassetteRequest
            {
                Method = request.Method.Method,
                Url = request.RequestUri?.ToString()
            },
            Response = new CassetteResponse
            {
                Status = (int)response.StatusCode,
                Body = body
            }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
    }

    private class CassetteDto
    {
        public CassetteRequest Request { get; set; }
        public CassetteResponse Response { get; set; }
    }

    private class CassetteRequest
    {
        public string Method { get; set; }
        public string Url { get; set; }
    }

    private class CassetteResponse
    {
        public int Status { get; set; }
        public string Body { get; set; }
        public Dictionary<string, string> Headers { get; set; }
    }
}
