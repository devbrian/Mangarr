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
        var response = new HttpResponseMessage((System.Net.HttpStatusCode)dto.Response.Status);

        // WR-14 (18-REVIEW): decode the body using the dto.Response.BodyIsBase64
        // flag. Text payloads (text/*, json, xml, javascript) round-trip via
        // StringContent verbatim; binary payloads (CBZ archives, gzip-pre-
        // decompression bytes, signed-binary responses) round-trip via
        // Base64 → ByteArrayContent so byte-level fidelity is preserved.
        if (dto.Response.BodyIsBase64 && dto.Response.Body != null)
        {
            var bytes = Convert.FromBase64String(dto.Response.Body);
            response.Content = new ByteArrayContent(bytes);
        }
        else
        {
            response.Content = new StringContent(dto.Response.Body ?? string.Empty);
        }

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
        // WR-14 (18-REVIEW): detect binary content types and base64-encode the
        // body instead of lossy-decoding through ReadAsStringAsync. Without
        // this guard a CBZ download or gzip-pre-decompression payload would
        // be silently corrupted on the record→replay round-trip and the
        // failure would surface as a cryptic decoding error at runtime read
        // time, not as a clean cassette-miss.
        var contentType = response.Content?.Headers?.ContentType?.MediaType;
        var isText = contentType != null && (
            contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase));

        string body;
        bool bodyIsBase64;
        if (response.Content == null)
        {
            body = null;
            bodyIsBase64 = false;
        }
        else if (isText)
        {
            body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            bodyIsBase64 = false;
        }
        else
        {
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            body = Convert.ToBase64String(bytes);
            bodyIsBase64 = true;
        }

        // BL-02 fix (18-13): persist Content-Type + non-sensitive response headers so
        // LoadFromDiskAsync replay produces the same MediaType the live response had.
        // T-18-01 mitigation: IsSensitiveHeader filters Authorization / Cookie /
        // Set-Cookie / X-Api-Key so sensitive bytes never reach the on-disk JSON.
        var headers = new Dictionary<string, string>();
        if (response.Content?.Headers != null)
        {
            foreach (var h in response.Content.Headers)
            {
                if (!IsSensitiveHeader(h.Key))
                {
                    headers[h.Key] = string.Join(", ", h.Value);
                }
            }
        }

        foreach (var h in response.Headers)
        {
            if (!IsSensitiveHeader(h.Key))
            {
                headers[h.Key] = string.Join(", ", h.Value);
            }
        }

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
                Body = body,
                BodyIsBase64 = bodyIsBase64,
                Headers = headers.Count > 0 ? headers : null
            }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
    }

    private static bool IsSensitiveHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase);

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

        // WR-14 (18-REVIEW): when true, Body is a Base64-encoded byte sequence
        // (binary payload — CBZ, signed-binary, gzip-pre-decompression bytes,
        // etc.); when false, Body is a UTF-8 string (text/*, json, xml,
        // javascript). LoadFromDiskAsync decodes accordingly so byte-level
        // fidelity is preserved through the record→replay round-trip.
        public bool BodyIsBase64 { get; set; }

        public Dictionary<string, string> Headers { get; set; }
    }
}
