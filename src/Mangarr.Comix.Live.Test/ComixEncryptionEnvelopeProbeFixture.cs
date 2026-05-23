using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Test.Common.Categories;
using PuppeteerSharp;

namespace Mangarr.Comix.Live.Test
{
    /// <summary>
    /// 2026-05-22 PR #244 follow-up — Encryption-Envelope Triangulation Probe.
    ///
    /// <para>
    /// PR #244 fixed captureToken — we cleanly capture the `_=&lt;token&gt;` query
    /// parameter off the bundle's outgoing /api/v1/manga/{hid}/chapters request.
    /// However, the .NET HttpClient relay (with cookies + UA forwarded) still gets
    /// back the encrypted `{"e": "&lt;base64&gt;"}` envelope instead of plaintext.
    /// </para>
    ///
    /// <para>
    /// This explicit-only probe triangulates 3 of 4 hypotheses in a single run:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>H4 — axios-interceptor in-bundle decryption</b>: the bundle ships
    ///         an axios response-interceptor that decrypts <c>{e:...}</c> envelopes
    ///         inline. <c>fetch()</c> bypasses axios; <c>axios.get(...)</c> via the
    ///         bundle's instance hits the interceptor and returns plaintext.</item>
    ///   <item><b>H1 — TLS JA3 fingerprint gate</b>: page.fetch (Chromium TLS stack)
    ///         vs .NET HttpClient (SChannel/OpenSSL) produce different JA3
    ///         fingerprints; if comix.to gates on JA3, page.fetch returns plaintext
    ///         while .NET-relay gets encrypted.</item>
    ///   <item><b>H3 — single-use token</b>: the browser consumes the token in its
    ///         own outgoing request; our relay reuses an already-consumed token and
    ///         gets the encrypted-fallback response.</item>
    /// </list>
    ///
    /// <para>
    /// Output JSON saved to <c>.planning/debug/evidence/comix-signer-rotation/h4-h1-h3-probe-&lt;timestamp&gt;.json</c>
    /// for analysis.
    /// </para>
    ///
    /// Sonarr divergence: no Sonarr peer. Manga-side diagnostic fixture.
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("encryption-envelope triangulation probe — runs only when explicitly invoked")]
    public class ComixEncryptionEnvelopeProbeFixture
    {
        private const string ComixBase = "https://comix.to";
        private const string TargetHid = "mr3m0";
        private const int CaptureTimeoutSeconds = 30;

        [Test]
        public async Task Probe_H4_H1_H3_in_one_pass()
        {
            var result = new ProbeResult { Timestamp = DateTimeOffset.UtcNow };
            string browserOwnBody = null;
            int? browserOwnStatus = null;
            var executablePath = ResolveChromiumExecutable();
            result.ChromiumExecutable = executablePath;

            var launchOptions = new LaunchOptions
            {
                Headless = true,
                ExecutablePath = executablePath,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                },
            };

            using var browser = await Puppeteer.LaunchAsync(launchOptions);
            var page = await browser.NewPageAsync();

            var userAgent = await page.EvaluateExpressionAsync<string>("navigator.userAgent");
            result.UserAgent = userAgent;

            var tokenTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            string targetRequestUrl = null;

            EventHandler<RequestEventArgs> reqHandler = null;
            reqHandler = async (s, e) =>
            {
                try
                {
                    var req = e.Request;
                    if (req == null)
                    {
                        return;
                    }

                    var url = req.Url ?? string.Empty;
                    if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                    {
                        if (parsed.AbsolutePath.EndsWith($"/api/v1/manga/{TargetHid}/chapters", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = ExtractQueryParam(parsed.Query, "_");
                            if (!string.IsNullOrEmpty(token))
                            {
                                tokenTcs.TrySetResult(token);
                                targetRequestUrl = url;
                            }
                        }

                        var host = parsed.Host ?? string.Empty;
                        var path = parsed.AbsolutePath ?? string.Empty;
                        if (host.IndexOf("comix.to", StringComparison.OrdinalIgnoreCase) >= 0
                            && (path.IndexOf(".js", StringComparison.OrdinalIgnoreCase) >= 0
                                || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                                || path.StartsWith("/title/", StringComparison.OrdinalIgnoreCase)
                                || path.StartsWith("/chapters/", StringComparison.OrdinalIgnoreCase)
                                || path == "/"))
                        {
                            await req.ContinueAsync();
                            return;
                        }
                    }

                    await req.AbortAsync();
                }
                catch
                {
                    // swallow
                }
            };

            EventHandler<RequestEventArgs> finishedHandler = null;
            finishedHandler = async (s, e) =>
            {
                try
                {
                    var req = e.Request;
                    if (req?.Url == null)
                    {
                        return;
                    }

                    if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var parsed))
                    {
                        return;
                    }

                    if (!parsed.AbsolutePath.EndsWith($"/api/v1/manga/{TargetHid}/chapters", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    var resp = req.Response;
                    if (resp == null)
                    {
                        return;
                    }

                    browserOwnStatus = (int)resp.Status;
                    try
                    {
                        browserOwnBody = await resp.TextAsync();
                    }
                    catch (Exception bex)
                    {
                        browserOwnBody = "<TextAsync threw: " + bex.GetType().Name + " " + bex.Message + ">";
                    }
                }
                catch
                {
                    // swallow
                }
            };

            await page.SetRequestInterceptionAsync(true);
            page.Request += reqHandler;
            page.RequestFinished += finishedHandler;

            try
            {
                _ = page.GoToAsync(
                    $"{ComixBase}/title/{TargetHid}",
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });

                var delay = Task.Delay(TimeSpan.FromSeconds(CaptureTimeoutSeconds));
                var winner = await Task.WhenAny(tokenTcs.Task, delay);
                if (winner != tokenTcs.Task)
                {
                    result.Error = "captureToken timed out after 30s";
                    SaveResult(result);
                    Assert.Fail(result.Error);
                    return;
                }

                var capturedToken = await tokenTcs.Task;
                result.CapturedToken = capturedToken;
                result.TargetRequestUrl = targetRequestUrl;

                // Wait briefly for the browser's own response to finish so finishedHandler populates.
                await Task.Delay(TimeSpan.FromSeconds(3));

                var apiUrl = $"/api/v1/manga/{TargetHid}/chapters?_={Uri.EscapeDataString(capturedToken)}";

                // ----- Step A: page.fetch() with the SAME captured token -----
                try
                {
                    var fetchScript =
                        "(async () => { try { const r = await fetch('" + apiUrl.Replace("'", "\\'") +
                        "', { credentials: 'include', headers: { Accept: 'application/json' } }); " +
                        "const t = await r.text(); return JSON.stringify({ status: r.status, body: t.slice(0, 500), len: t.length }); } " +
                        "catch (e) { return JSON.stringify({ error: e.toString() }); } })()";
                    var fetchJson = await page.EvaluateExpressionAsync<string>(fetchScript);
                    result.PageFetch = fetchJson;
                }
                catch (Exception ex)
                {
                    result.PageFetch = "EVAL_THREW: " + ex.GetType().Name + ": " + ex.Message;
                }

                // ----- Step B: discover the bundle's axios instance + call .get() with the token -----
                try
                {
                    var axiosScript = BuildAxiosProbeScript(apiUrl);
                    var axiosJson = await page.EvaluateExpressionAsync<string>(axiosScript);
                    result.PageAxios = axiosJson;
                }
                catch (Exception ex)
                {
                    result.PageAxios = "EVAL_THREW: " + ex.GetType().Name + ": " + ex.Message;
                }

                // ----- Step C: .NET HttpClient relay (CURRENT PR #244 shipping path) -----
                try
                {
                    var cookies = await page.GetCookiesAsync(ComixBase);
                    var cookieHeader = cookies != null && cookies.Length > 0
                        ? string.Join("; ", cookies.Select(c => c.Name + "=" + c.Value))
                        : null;

                    using var httpClient = new HttpClient();
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{ComixBase}{apiUrl}");
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    req.Headers.Referrer = new Uri(ComixBase + "/");
                    if (!string.IsNullOrEmpty(userAgent))
                    {
                        req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                    }

                    if (!string.IsNullOrEmpty(cookieHeader))
                    {
                        req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                    }

                    using var resp = await httpClient.SendAsync(req);
                    var body = await resp.Content.ReadAsStringAsync();
                    result.NetRelay = JsonSerializer.Serialize(new
                    {
                        status = (int)resp.StatusCode,
                        body = body.Length > 500 ? body[..500] : body,
                        len = body.Length,
                    });
                }
                catch (Exception ex)
                {
                    result.NetRelay = "NET_THREW: " + ex.GetType().Name + ": " + ex.Message;
                }

                // ----- Step D: browser's OWN initial response (captured via RequestFinished) -----
                result.BrowserOwnStatus = browserOwnStatus;
                result.BrowserOwnBody = browserOwnBody != null && browserOwnBody.Length > 500
                    ? browserOwnBody[..500]
                    : browserOwnBody;
                result.BrowserOwnBodyLen = browserOwnBody?.Length;

                // ----- Analysis: compute hypothesis verdicts -----
                result.PageFetchIsEncrypted = IsEncrypted(result.PageFetch);
                result.PageAxiosIsEncrypted = IsEncrypted(result.PageAxios);
                result.NetRelayIsEncrypted = IsEncrypted(result.NetRelay);
                result.BrowserOwnIsEncrypted = browserOwnBody != null ? IsEncrypted(browserOwnBody) : (bool?)null;

                result.Verdicts = ComputeVerdicts(result);

                SaveResult(result);

                TestContext.WriteLine("================ PROBE RESULT ================");
                TestContext.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("==============================================");
            }
            finally
            {
                page.Request -= reqHandler;
                page.RequestFinished -= finishedHandler;
                try
                {
                    await page.SetRequestInterceptionAsync(false);
                }
                catch
                {
                    // swallow
                }

                try
                {
                    await browser.CloseAsync();
                }
                catch
                {
                    // swallow
                }
            }
        }

        private static string BuildAxiosProbeScript(string apiUrl)
        {
            var apiUrlEsc = apiUrl.Replace("'", "\\'");
            return @"
(async () => {
  try {
    let axiosRef = null;
    let axiosPath = null;
    const seen = new Set();

    function isAxiosLike(v) {
      try {
        return v && typeof v === 'function' && typeof v.get === 'function' &&
               v.interceptors && v.interceptors.response &&
               typeof v.interceptors.response.use === 'function';
      } catch (_) { return false; }
    }

    const candidates = ['axios', '__axios__', '_axios', 'http', '$http', 'request', '_request'];
    for (const k of candidates) {
      try {
        if (isAxiosLike(window[k])) { axiosRef = window[k]; axiosPath = 'window.' + k; break; }
      } catch (_) {}
    }

    if (!axiosRef) {
      for (const k of Object.keys(window)) {
        if (seen.has(k)) continue;
        seen.add(k);
        try {
          const ns = window[k];
          if (!ns || (typeof ns !== 'object' && typeof ns !== 'function')) continue;
          if (isAxiosLike(ns)) { axiosRef = ns; axiosPath = 'window.' + k; break; }
          for (const sk of Object.keys(ns)) {
            try {
              if (isAxiosLike(ns[sk])) { axiosRef = ns[sk]; axiosPath = 'window.' + k + '.' + sk; break; }
            } catch (_) {}
          }
          if (axiosRef) break;
        } catch (_) {}
      }
    }

    if (!axiosRef) {
      const wpKeys = Object.keys(window).filter(k => k.startsWith('webpackChunk'));
      return JSON.stringify({ axiosFound: false, axiosPath: null, wpChunkKeys: wpKeys, candidatesTried: candidates });
    }

    try {
      const resp = await axiosRef.get('" + apiUrlEsc + @"');
      const dataStr = typeof resp.data === 'string' ? resp.data : JSON.stringify(resp.data);
      return JSON.stringify({
        axiosFound: true,
        axiosPath: axiosPath,
        status: resp.status,
        body: dataStr.slice(0, 500),
        len: dataStr.length,
      });
    } catch (apiEx) {
      return JSON.stringify({ axiosFound: true, axiosPath: axiosPath, apiError: apiEx.toString() });
    }
  } catch (outerEx) {
    return JSON.stringify({ outerError: outerEx.toString() });
  }
})()";
        }

        private static bool IsEncrypted(string responseJson)
        {
            if (string.IsNullOrEmpty(responseJson))
            {
                return false;
            }

            return responseJson.Contains("\"e\":", StringComparison.OrdinalIgnoreCase)
                && !responseJson.Contains("\"items\"", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> ComputeVerdicts(ProbeResult r)
        {
            var v = new Dictionary<string, string>();

            if (r.PageAxios != null && r.PageAxios.Contains("\"axiosFound\":true"))
            {
                v["H4"] = r.PageAxiosIsEncrypted
                    ? "RULED OUT - axios discovered but its .get() also returned encrypted envelope"
                    : "CONFIRMED - axios discovered; .get() returned plaintext (interceptor decrypts)";
            }
            else
            {
                v["H4"] = "INDETERMINATE - axios instance not discoverable via window walk";
            }

            if (r.PageFetchIsEncrypted && r.NetRelayIsEncrypted)
            {
                v["H1"] = "RULED OUT - both Chromium fetch + .NET relay return encrypted (encryption is not JA3-conditional)";
            }
            else if (!r.PageFetchIsEncrypted && r.NetRelayIsEncrypted)
            {
                v["H1"] = "POTENTIAL LEAD - Chromium fetch returned plaintext while .NET relay returned encrypted (JA3 differentiator)";
            }
            else
            {
                v["H1"] = "INDETERMINATE - see PageFetch/NetRelay raw values";
            }

            if (r.BrowserOwnIsEncrypted == true)
            {
                v["H3"] = "RULED OUT - browser's own initial response was ALSO encrypted (token cannot be single-use)";
            }
            else if (r.BrowserOwnIsEncrypted == false && r.NetRelayIsEncrypted)
            {
                v["H3"] = "POTENTIAL LEAD - browser's own response was plaintext but relay (same token) was encrypted (token might be single-use)";
            }
            else
            {
                v["H3"] = "INDETERMINATE - browser body not captured";
            }

            return v;
        }

        private static void SaveResult(ProbeResult result)
        {
            try
            {
                var dir = Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    ".planning",
                    "debug",
                    "evidence",
                    "comix-signer-rotation");
                dir = Path.GetFullPath(dir);
                Directory.CreateDirectory(dir);
                var ts = result.Timestamp.ToString("yyyyMMdd-HHmmss");
                var file = Path.Combine(dir, $"h4-h1-h3-probe-{ts}.json");
                File.WriteAllText(file, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("Probe result saved to: " + file);
            }
            catch (Exception ex)
            {
                TestContext.WriteLine("Could not save probe result: " + ex.Message);
            }
        }

        private static string ExtractQueryParam(string query, string name)
        {
            if (string.IsNullOrEmpty(query))
            {
                return null;
            }

            var trimmed = query.StartsWith("?", StringComparison.Ordinal) ? query[1..] : query;
            foreach (var pair in trimmed.Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }

                var key = pair[..eq];
                if (string.Equals(key, name, StringComparison.Ordinal))
                {
                    return Uri.UnescapeDataString(pair[(eq + 1)..]);
                }
            }

            return null;
        }

        private static string ResolveChromiumExecutable()
        {
            var explicitPath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
            {
                return explicitPath;
            }

            var cache = Environment.GetEnvironmentVariable("PUPPETEER_CACHE_DIR");
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(cache))
            {
                candidates.Add(cache);
            }

            candidates.Add("/opt/mangarr-chromium");
            var home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(home))
            {
                candidates.Add(Path.Combine(home, ".cache", "mangarr-chromium"));
            }

            var lad = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(lad))
            {
                candidates.Add(Path.Combine(lad, "mangarr-chromium"));
            }

            foreach (var dir in candidates)
            {
                try
                {
                    var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = dir });
                    var installed = fetcher.GetInstalledBrowsers().FirstOrDefault();
                    var exec = installed?.GetExecutablePath();
                    if (!string.IsNullOrEmpty(exec) && File.Exists(exec))
                    {
                        return exec;
                    }
                }
                catch
                {
                    // try next
                }
            }

            return null;
        }

        private sealed class ProbeResult
        {
            public DateTimeOffset Timestamp { get; set; }

            public string ChromiumExecutable { get; set; }

            public string UserAgent { get; set; }

            public string CapturedToken { get; set; }

            public string TargetRequestUrl { get; set; }

            public string PageFetch { get; set; }

            public string PageAxios { get; set; }

            public string NetRelay { get; set; }

            public int? BrowserOwnStatus { get; set; }

            public string BrowserOwnBody { get; set; }

            public int? BrowserOwnBodyLen { get; set; }

            public bool PageFetchIsEncrypted { get; set; }

            public bool PageAxiosIsEncrypted { get; set; }

            public bool NetRelayIsEncrypted { get; set; }

            public bool? BrowserOwnIsEncrypted { get; set; }

            public Dictionary<string, string> Verdicts { get; set; }

            public string Error { get; set; }
        }
    }
}
