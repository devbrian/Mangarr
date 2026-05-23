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
    /// 2026-05-23 — Investigation Phase 3, iteration 4 — exhaustive trampoline call.
    ///
    /// <para>
    /// Iter 2/3 confirmed: VMP-protected bundle; <c>vmX_*</c> is closure-scope. The
    /// namespace fns (Zi/Hi/Wi/Fi/Ri/qi/Ei/Ai/Ti/Ii/Pi/xi/Bi/Vi/Mi/Ci/yi) ARE callable
    /// directly — each is a trampoline that invokes the closure-scoped VM with a fixed
    /// opcode. Iter 2 Probe G tested each with [e-value, full-env-str, env-obj,
    /// response-data-shape] but only checked output against narrow heuristics that
    /// missed candidates returning short or different-shape outputs.
    /// </para>
    ///
    /// <para>
    /// This iter exhaustively calls every fn with every combination of arg + records
    /// EVERY output (no filtering). Then we cross-reference the captured encrypted
    /// envelope against every output to find the decryption oracle.
    /// </para>
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("exhaustive trampoline call probe — runs only when explicitly invoked")]
    public class ComixOracleObserveProbeFixture
    {
        private const string ComixBase = "https://comix.to";
        private const string TargetHid = "mr3m0";
        private const int CaptureTimeoutSeconds = 30;
        private const int PostCaptureWaitSeconds = 10;

        [Test]
        public async Task Exhaustive_trampoline_call_with_all_shapes()
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

            string capturedToken = null;
            string browserOwnEncrypted = null;

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

                    if (parsed.AbsolutePath.EndsWith(
                            $"/api/v1/manga/{TargetHid}/chapters",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        var token = ExtractQueryParam(parsed.Query, "_");
                        if (!string.IsNullOrEmpty(token) && capturedToken == null)
                        {
                            capturedToken = token;
                        }

                        if (req.Response != null && browserOwnEncrypted == null)
                        {
                            try
                            {
                                browserOwnEncrypted = await req.Response.TextAsync();
                            }
                            catch
                            {
                                // swallow
                            }
                        }
                    }
                }
                catch
                {
                    // swallow
                }
            };

            page.RequestFinished += finishedHandler;

            try
            {
                _ = page.GoToAsync(
                    $"{ComixBase}/title/{TargetHid}",
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });

                // Wait for full bundle + chapter request to complete.
                var deadline = DateTimeOffset.UtcNow.AddSeconds(CaptureTimeoutSeconds + PostCaptureWaitSeconds);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(500);
                    if (browserOwnEncrypted != null
                        && DateTimeOffset.UtcNow > deadline.AddSeconds(-PostCaptureWaitSeconds))
                    {
                        break;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(PostCaptureWaitSeconds));

                if (string.IsNullOrEmpty(browserOwnEncrypted))
                {
                    Assert.Fail("Could not capture encrypted body");
                    return;
                }

                // Now call every trampoline with every arg shape and DUMP ALL outputs
                // unfiltered. Cross-reference happens post-hoc.
                var encryptedJs = browserOwnEncrypted
                    .Replace("\\", "\\\\")
                    .Replace("'", "\\'")
                    .Replace("\n", "\\n")
                    .Replace("\r", string.Empty);

                var exhaustiveResults = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const fullEnv = '" + encryptedJs + @"';
  let eValue;
  try { eValue = JSON.parse(fullEnv).e; } catch (_) { return JSON.stringify({err:'parse'}); }

  // Build the full list of fns to test.
  const targets = [];
  const nsKey = Object.keys(globalThis).find(k => k.startsWith('vmt_'));
  if (nsKey) {
    const ns = globalThis[nsKey];
    for (const k of Object.keys(ns)) {
      if (typeof ns[k] === 'function') {
        // Extract opcode from toString.
        const m = ns[k].toString().match(/vmX_[a-zA-Z0-9]+\((\d+)/);
        targets.push({ key: 'ns.' + k, opcode: m ? parseInt(m[1], 10) : null, fn: ns[k], fnLen: ns[k].length });
      }
    }
  }

  // Also include short-identifier top-level fns.
  for (const k of Object.keys(globalThis)) {
    if (/^[A-Z][a-z]?$/.test(k)) {
      const v = globalThis[k];
      if (typeof v === 'function') {
        targets.push({ key: 'global.' + k, opcode: null, fn: v, fnLen: v.length });
      }
    }
  }

  // Get cfg.
  let cfg = null;
  try {
    const meta = document.querySelector('meta[name=""cfg""]');
    if (meta) cfg = meta.getAttribute('content');
  } catch (_) {}

  // Comprehensive arg shapes.
  const argShapes = [
    ['e-value', [eValue]],
    ['full-env-str', [fullEnv]],
    ['env-obj', [{ e: eValue }]],
    ['env-obj-data', [{ data: { e: eValue } }]],
    ['env-obj-result', [{ result: { e: eValue } }]],
    ['e-value-twice', [eValue, eValue]],
    ['e-value+cfg', cfg ? [eValue, cfg] : null],
    ['cfg+e-value', cfg ? [cfg, eValue] : null],
    ['full-env+cfg', cfg ? [fullEnv, cfg] : null],
    ['env-obj+cfg', cfg ? [{ e: eValue }, cfg] : null],
  ].filter(x => x[1] !== null);

  const allOutputs = [];
  for (const t of targets) {
    for (const [shape, args] of argShapes) {
      try {
        const out = t.fn.apply(null, args);
        const outType = typeof out;
        let outRepr;
        let outLen;
        if (out == null) { outRepr = 'null'; outLen = 0; }
        else if (outType === 'string') { outRepr = out.slice(0, 250); outLen = out.length; }
        else if (outType === 'number' || outType === 'boolean') { outRepr = String(out); outLen = outRepr.length; }
        else if (outType === 'object') {
          try {
            const ser = JSON.stringify(out);
            outRepr = ser.slice(0, 350);
            outLen = ser.length;
          } catch (_) { outRepr = '<unserialisable>'; outLen = 0; }
        }
        else { outRepr = String(out).slice(0, 100); outLen = outRepr.length; }

        // Highlight if outputs are interesting: different from input AND non-empty.
        const isEcho = outRepr === eValue.slice(0, 250) || outRepr === fullEnv.slice(0, 250);
        const isNonEmpty = outLen > 0 && outRepr !== 'null' && outRepr !== 'undefined' && outRepr !== '0';

        if (isNonEmpty && !isEcho) {
          allOutputs.push({
            fn: t.key, opcode: t.opcode, fnLen: t.fnLen,
            shape, outType, outLen, outRepr,
          });
        }
      } catch (_) { /* skip */ }
    }
  }

  return JSON.stringify({
    targetCount: targets.length,
    cfgPresent: cfg != null,
    eValueLen: eValue.length,
    nonEchoOutputCount: allOutputs.length,
    allOutputs,
  });
})()
");

                var result = new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
                    ["CapturedToken"] = capturedToken,
                    ["BrowserOwnBodyLen"] = browserOwnEncrypted.Length,
                    ["BrowserOwnBodyPreview"] = browserOwnEncrypted.Length > 200 ? browserOwnEncrypted[..200] : browserOwnEncrypted,
                    ["ExhaustiveResults"] = exhaustiveResults,
                };

                SaveResult(result);

                TestContext.WriteLine("================ EXHAUSTIVE PROBE RESULT ================");
                TestContext.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("=========================================================");
            }
            finally
            {
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
                var file = Path.Combine(dir, $"oracle-exhaustive-probe-{ts}.json");
                File.WriteAllText(file, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("Exhaustive probe result saved to: " + file);
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
