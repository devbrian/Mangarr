using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Test.Common.Categories;
using PuppeteerSharp;

namespace Mangarr.Comix.Live.Test
{
    /// <summary>
    /// 2026-05-23 follow-up to ComixEncryptionEnvelopeProbeFixture.
    ///
    /// <para>
    /// The H4/H1/H3 probe revealed: ALL channels (page.fetch, .NET relay, browser's
    /// own response) return the SAME encrypted envelope <c>{"e":"&lt;base64&gt;"}</c>.
    /// Yet the page renders chapter lists correctly — therefore the bundle DOES
    /// decrypt envelopes; the decryption fn just isn't reachable from the well-known
    /// global names we checked.
    /// </para>
    ///
    /// <para>
    /// Inspecting <c>secure-tfgaak-*.js</c> showed the namespace is now
    /// <c>globalThis.vmt_95379</c> (NOT <c>vmf_*</c> per the old probe), and all
    /// its functions are ALSO mounted as direct globals <c>globalThis.{Mr, Ji, Ti,
    /// Ii, Pi, xi, Bi, Vi, Mi, Hi, ...}</c>. The bundle exports
    /// <c>export { E as i, Hi as n, Mr as r, B as t }</c>.
    /// </para>
    ///
    /// <para>
    /// This probe waits for the secure chunk to load, then:
    /// </para>
    /// <list type="number">
    ///   <item>Enumerates <c>Object.keys(globalThis)</c> looking for <c>vmt_*</c>.</item>
    ///   <item>Lists all properties of the discovered namespace.</item>
    ///   <item>Captures the encrypted envelope from the chapters API call.</item>
    ///   <item>Walks the namespace + the well-known globals (Mr, Ji, etc.) calling each
    ///         with the encrypted string + the encrypted object + various combinations,
    ///         looking for a return value that looks like a chapter list (contains
    ///         "items" or "chapter").</item>
    /// </list>
    ///
    /// <para>
    /// Output saved alongside the H4/H1/H3 probe in
    /// <c>.planning/debug/evidence/comix-signer-rotation/</c>.
    /// </para>
    ///
    /// Sonarr divergence: no Sonarr peer.
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("decryption-oracle discovery probe — runs only when explicitly invoked")]
    public class ComixDecryptionOracleProbeFixture
    {
        private const string ComixBase = "https://comix.to";
        private const string TargetHid = "mr3m0";
        private const int CaptureTimeoutSeconds = 30;
        private const int PostDclWaitSeconds = 10;

        [Test]
        public async Task Discover_decryption_oracle_in_secure_bundle()
        {
            var executablePath = ResolveChromiumExecutable();
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

            var tokenTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            string browserOwnBody = null;

            // page.Request fires as soon as the request is issued (URL is known here).
            // page.RequestFinished fires later (after response body downloads).
            // Splitting responsibilities: tokenTcs completes from page.Request so it
            // doesn't race the 30s CaptureTimeoutSeconds Task.WhenAny when the body
            // download is slow. RequestFinished only captures the encrypted body.
            EventHandler<RequestEventArgs> requestHandler = null;
            requestHandler = (s, e) =>
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

                    var token = ExtractQueryParam(parsed.Query, "_");
                    if (!string.IsNullOrEmpty(token))
                    {
                        tokenTcs.TrySetResult(token);
                    }
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

                    if (req.Response != null && browserOwnBody == null)
                    {
                        try
                        {
                            browserOwnBody = await req.Response.TextAsync();
                        }
                        catch
                        {
                            // swallow
                        }
                    }
                }
                catch
                {
                    // swallow
                }
            };

            page.Request += requestHandler;
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
                    Assert.Fail("captureToken timed out after 30s");
                    return;
                }

                // Wait extra time for secure-*.js chunk to finish loading + register globals.
                await Task.Delay(TimeSpan.FromSeconds(PostDclWaitSeconds));

                // Wait briefly to ensure browserOwnBody is populated.
                for (var i = 0; i < 50 && browserOwnBody == null; i++)
                {
                    await Task.Delay(100);
                }

                if (string.IsNullOrEmpty(browserOwnBody))
                {
                    Assert.Fail("Could not capture browser's encrypted body");
                    return;
                }

                // Probe 1: list all globalThis keys matching vmt_*.
                var probe1 = await page.EvaluateExpressionAsync<string>(@"
JSON.stringify(Object.keys(globalThis).filter(k => k.startsWith('vmt_') || k.startsWith('vmf_')))
");

                // Probe 2: pick the first vmt_* namespace + list its properties.
                var probe2 = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const ns_key = Object.keys(globalThis).find(k => k.startsWith('vmt_') || k.startsWith('vmf_'));
  if (!ns_key) return JSON.stringify({ found: false });
  const ns = globalThis[ns_key];
  const props = {};
  for (const p of Object.keys(ns)) {
    try {
      const v = ns[p];
      props[p] = { type: typeof v, length: typeof v === 'function' ? v.length : undefined };
    } catch (_) { props[p] = { error: true }; }
  }
  return JSON.stringify({ found: true, key: ns_key, props });
})()
");

                // Probe 3: list well-known Mr, Ji, Ti, Hi, etc. globals.
                var probe3 = await page.EvaluateExpressionAsync<string>(@"
JSON.stringify(['Mr','Ji','Ti','Ii','Pi','xi','Bi','Vi','Mi','Hi','Ai','Ci','Di','Ei','Fi','Ni','Oi','Ri','Si','Ui','Wi','Zi','B','E'].map(n => ({ name: n, type: typeof globalThis[n], length: typeof globalThis[n] === 'function' ? globalThis[n].length : undefined })))
");

                // Probe 4: try each fn in the namespace with the encrypted body in
                // various shapes, looking for one whose return contains "items" or
                // "chapter" (decoded chapter list signal).
                var probe4 = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const ns_key = Object.keys(globalThis).find(k => k.startsWith('vmt_') || k.startsWith('vmf_'));
  if (!ns_key) return JSON.stringify({ found: false });

  const ns = globalThis[ns_key];
  const sample_encrypted = '{""e"":""9PunP4BFQkK7""}';
  const sample_e_value = '9PunP4BFQkK7';

  const candidates = [];
  for (const p of Object.keys(ns)) {
    const v = ns[p];
    if (typeof v !== 'function') continue;

    const attempts = [
      ['string', sample_encrypted],
      ['e-value', sample_e_value],
      ['object', { e: sample_e_value }],
      ['response-like', { data: { e: sample_e_value } }],
      ['no-args', undefined],
    ];

    for (const [shape, arg] of attempts) {
      try {
        const out = arg === undefined ? v() : v(arg);
        const outStr = typeof out === 'string' ? out : JSON.stringify(out);
        if (outStr && (outStr.includes('items') || outStr.includes('chapter') || outStr.length > 200)) {
          candidates.push({ prop: p, argShape: shape, outLen: outStr.length, sample: outStr.slice(0, 300) });
        }
      } catch (e) {
        // skip
      }
    }
  }

  return JSON.stringify({ found: true, key: ns_key, candidates });
})()
");

                // Probe 5: also try well-known top-level fns (Mr, Ji, Ti, etc.) the
                // same way — they're re-exported through globalThis.
                var probe5 = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const names = ['Mr','Ji','Ti','Ii','Pi','xi','Bi','Vi','Mi','Hi','Ai','Ci','Di','Ei','Fi','Ni','Oi','Ri','Si','Ui','Wi','Zi','B','E'];
  const sample_encrypted = '{""e"":""9PunP4BFQkK7""}';
  const sample_e_value = '9PunP4BFQkK7';
  const results = [];

  for (const n of names) {
    const fn = globalThis[n];
    if (typeof fn !== 'function') continue;

    const attempts = [
      ['string', sample_encrypted],
      ['e-value', sample_e_value],
      ['object', { e: sample_e_value }],
      ['response-like', { data: { e: sample_e_value } }],
    ];

    for (const [shape, arg] of attempts) {
      try {
        const out = fn(arg);
        const outStr = typeof out === 'string' ? out : JSON.stringify(out);
        if (outStr && (outStr.includes('items') || outStr.includes('chapter') || outStr.length > 200)) {
          results.push({ name: n, argShape: shape, outLen: outStr.length, sample: outStr.slice(0, 300) });
        }
      } catch (_) {}
    }
  }
  return JSON.stringify(results);
})()
");

                // Probe 6: dump a survey of globalThis keys.
                var probe6 = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const allKeys = Object.keys(globalThis);
  const filtered = allKeys.filter(k => !['Math','JSON','Object','Array','String','Number','Boolean','Date','RegExp','Promise','Reflect','Symbol','Proxy','Intl','Error','Map','Set','WeakMap','WeakSet','Atomics','console','globalThis','undefined','window','self','document','location','navigator','history','localStorage','sessionStorage','indexedDB','performance','crypto','setTimeout','setInterval','clearTimeout','clearInterval','requestAnimationFrame','cancelAnimationFrame','fetch','Headers','Request','Response','XMLHttpRequest','WebSocket','MessageChannel','MessagePort','URL','URLSearchParams','TextEncoder','TextDecoder','FormData','Blob','File','FileReader','FileList','Image','Audio','HTMLElement','HTMLDivElement','Element','Document','Window','Node','Event','EventTarget','MouseEvent','KeyboardEvent','Range','Selection','MutationObserver','IntersectionObserver','ResizeObserver','PerformanceObserver'].includes(k));
  return JSON.stringify({ total: allKeys.length, filtered_count: filtered.length, first_50: filtered.slice(0, 50) });
})()
");

                // Probe 7: check for monkey-patched fetch/XHR/Response.
                var probe7 = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const checks = {};
  checks.fetch_isNative = window.fetch.toString().includes('[native code]');
  checks.fetch_first200 = window.fetch.toString().slice(0, 200);
  try {
    checks.Response_json_isNative = Response.prototype.json.toString().includes('[native code]');
    checks.Response_text_isNative = Response.prototype.text.toString().includes('[native code]');
  } catch (e) { checks.error = e.toString(); }
  try {
    checks.XHR_send_isNative = XMLHttpRequest.prototype.send.toString().includes('[native code]');
    checks.XHR_open_isNative = XMLHttpRequest.prototype.open.toString().includes('[native code]');
    const xhrResponseDesc = Object.getOwnPropertyDescriptor(XMLHttpRequest.prototype, 'response');
    checks.XHR_response_descriptor = xhrResponseDesc ? {
      hasGetter: typeof xhrResponseDesc.get === 'function',
      getter_isNative: xhrResponseDesc.get ? xhrResponseDesc.get.toString().includes('[native code]') : null,
      getter_first200: xhrResponseDesc.get ? xhrResponseDesc.get.toString().slice(0, 200) : null,
    } : null;
  } catch (e) { checks.xhrError = e.toString(); }
  return JSON.stringify(checks);
})()
");

                var result = new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
                    ["BrowserEncryptedBody"] = browserOwnBody != null && browserOwnBody.Length > 500 ? browserOwnBody[..500] : browserOwnBody,
                    ["BrowserEncryptedBodyLen"] = browserOwnBody?.Length,
                    ["Probe1_vmtNamespaces"] = probe1,
                    ["Probe2_namespaceProps"] = probe2,
                    ["Probe3_wellKnownGlobals"] = probe3,
                    ["Probe4_oracleSearch_inNamespace"] = probe4,
                    ["Probe5_oracleSearch_topLevel"] = probe5,
                    ["Probe6_globalKeysSurvey"] = probe6,
                    ["Probe7_interceptorHooks"] = probe7,
                };

                SaveResult(result);

                TestContext.WriteLine("================ ORACLE PROBE RESULT ================");
                TestContext.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("=====================================================");
            }
            finally
            {
                page.Request -= requestHandler;
                page.RequestFinished -= finishedHandler;
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

        private static void SaveResult(object result)
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
                var ts = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
                var file = Path.Combine(dir, $"oracle-probe-{ts}.json");
                File.WriteAllText(file, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("Oracle probe result saved to: " + file);
            }
            catch (Exception ex)
            {
                TestContext.WriteLine("Could not save: " + ex.Message);
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

                if (string.Equals(pair[..eq], name, StringComparison.Ordinal))
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

            var candidates = new List<string>();
            var cache = Environment.GetEnvironmentVariable("PUPPETEER_CACHE_DIR");
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
    }
}
