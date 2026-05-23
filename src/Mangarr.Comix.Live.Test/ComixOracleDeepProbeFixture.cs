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
    /// 2026-05-23 — Investigation Phase 3 deep instrumentation probe.
    ///
    /// <para>
    /// Phase 2 confirmed: encryption envelope <c>{"e":"&lt;base64&gt;"}</c> is universal
    /// across all HTTP paths with the same captured token (page.fetch, .NET relay, browser
    /// own response). The decryption layer sits ABOVE the HTTP primitive layer, inside the
    /// bundle. Simple single-arg fuzzing of <c>vmt_95379</c> namespace fns returned zero
    /// plaintext candidates.
    /// </para>
    ///
    /// <para>
    /// First iteration of this probe wrapped <c>Function.prototype.call</c> globally; the
    /// page never bootstrapped (only 8563 calls, no vmt_* namespace ever appeared). The
    /// wrap broke the bundle. This iteration uses a LIGHTER, more passive approach:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Probe B only (PRE-bundle)</b> — only hook Response.prototype.text/json
    ///         and XMLHttpRequest.responseText. These are called LOW-FREQUENCY (once per
    ///         HTTP response) so the overhead is negligible.</item>
    ///   <item><b>Probe A2 (POST-bundle)</b> — wrap only functions on the
    ///         <c>vmt_*</c> namespace + well-known top-level fns. These get called more
    ///         often but the overhead is bounded by the namespace size.</item>
    ///   <item><b>Probe F — heuristic source scoring</b> — toString() every fn on the
    ///         namespace + look for atob/JSON.parse/Uint8Array/charCodeAt patterns.</item>
    ///   <item><b>Probe G — invoke top candidates</b> — call each high-scored fn with the
    ///         captured encrypted body in many arg shapes; look for plaintext output.</item>
    /// </list>
    ///
    /// <para>
    /// Output saved to <c>.planning/debug/evidence/comix-signer-rotation/oracle-deep-probe-&lt;ts&gt;.json</c>.
    /// </para>
    ///
    /// Sonarr divergence: no Sonarr peer.
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("decryption-oracle DEEP discovery probe — runs only when explicitly invoked")]
    public class ComixOracleDeepProbeFixture
    {
        private const string ComixBase = "https://comix.to";
        private const string TargetHid = "mr3m0";
        private const int CaptureTimeoutSeconds = 30;
        private const int PostCaptureWaitSeconds = 15;

        [Test]
        public async Task Discover_oracle_via_deep_instrumentation()
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

            // Probe B only — install LIGHT hooks BEFORE bundle loads. These are
            // low-frequency (one call per HTTP response) so they don't break the page
            // by adding per-call overhead the way wrapping Function.prototype.call did.
            const string lightProbeInstall = @"
(function() {
  if (window.__comixProbe) return;
  const state = {
    responseReads: [],          // every fetch().text() / fetch().json() / XHR call
    plaintextHits: [],          // responses where the body looks like JSON (not envelope)
    fnInvocations: [],          // narrowed call-log filled in by Probe A2 (post-bundle)
  };
  window.__comixProbe = state;

  const isEncryptedEnvelope = (s) => {
    if (typeof s !== 'string') return false;
    const t = s.trim();
    if (t.length < 10) return false;
    if (!t.startsWith('{')) return false;
    // Quick heuristic: {""e"":""..."" envelope.
    return /^\{\s*""e""\s*:\s*""/.test(t);
  };

  const isPlaintext = (s) => {
    if (typeof s !== 'string') return false;
    const t = s.trim();
    if (t.length < 10) return false;
    if (!(t.startsWith('{') || t.startsWith('['))) return false;
    return !isEncryptedEnvelope(s);
  };

  // Probe B — Response.prototype hooks.
  const origText = Response.prototype.text;
  const origJson = Response.prototype.json;

  Response.prototype.text = async function() {
    const result = await origText.call(this);
    try {
      if (state.responseReads.length < 100) {
        const entry = {
          via: 'text',
          url: this.url,
          status: this.status,
          isPlaintext: isPlaintext(result),
          isEnvelope: isEncryptedEnvelope(result),
          len: typeof result === 'string' ? result.length : null,
          preview: typeof result === 'string' ? result.slice(0, 150) : null,
        };
        state.responseReads.push(entry);
        if (entry.isPlaintext && entry.url && entry.url.indexOf('/api/v1/') >= 0) {
          state.plaintextHits.push(entry);
        }
      }
    } catch (_) {}
    return result;
  };

  Response.prototype.json = async function() {
    const result = await origJson.call(this);
    try {
      if (state.responseReads.length < 100) {
        const ser = result != null ? JSON.stringify(result) : null;
        const entry = {
          via: 'json',
          url: this.url,
          status: this.status,
          isObj: typeof result === 'object',
          len: ser ? ser.length : null,
          preview: ser ? ser.slice(0, 150) : null,
        };
        state.responseReads.push(entry);
        // .json() returns parsed object — definitionally plaintext.
        if (this.url && this.url.indexOf('/api/v1/') >= 0) {
          state.plaintextHits.push(entry);
        }
      }
    } catch (_) {}
    return result;
  };

  // Probe B′ — XHR hooks.
  try {
    const xhrProto = XMLHttpRequest.prototype;
    const respDesc = Object.getOwnPropertyDescriptor(xhrProto, 'response');
    const respTextDesc = Object.getOwnPropertyDescriptor(xhrProto, 'responseText');
    if (respDesc && respDesc.get) {
      const origGet = respDesc.get;
      Object.defineProperty(xhrProto, 'response', {
        configurable: true,
        get: function() {
          const v = origGet.call(this);
          try {
            if (state.responseReads.length < 100) {
              const ser = typeof v === 'string' ? v : (v ? JSON.stringify(v) : null);
              const entry = {
                via: 'xhr.response',
                url: this.responseURL,
                status: this.status,
                isObj: typeof v === 'object',
                isPlaintext: typeof v === 'string' ? isPlaintext(v) : false,
                isEnvelope: typeof v === 'string' ? isEncryptedEnvelope(v) : false,
                len: ser ? ser.length : null,
                preview: ser ? ser.slice(0, 150) : null,
              };
              state.responseReads.push(entry);
            }
          } catch (_) {}
          return v;
        }
      });
    }
    if (respTextDesc && respTextDesc.get) {
      const origGetText = respTextDesc.get;
      Object.defineProperty(xhrProto, 'responseText', {
        configurable: true,
        get: function() {
          const v = origGetText.call(this);
          try {
            if (state.responseReads.length < 100) {
              const entry = {
                via: 'xhr.responseText',
                url: this.responseURL,
                status: this.status,
                isPlaintext: isPlaintext(v),
                isEnvelope: isEncryptedEnvelope(v),
                len: typeof v === 'string' ? v.length : null,
                preview: typeof v === 'string' ? v.slice(0, 150) : null,
              };
              state.responseReads.push(entry);
            }
          } catch (_) {}
          return v;
        }
      });
    }
  } catch (_) {}
})();
";

            await page.EvaluateExpressionOnNewDocumentAsync(lightProbeInstall);

            // Optional: capture token + browser body for cross-reference.
            string capturedToken = null;
            string browserOwnEncrypted = null;
            int? browserOwnStatus = null;

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
                                browserOwnStatus = (int?)req.Response.Status;
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

                // Wait long enough for the page to fully bootstrap, fire the API call,
                // receive the encrypted envelope, and run the decryption path.
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

                // Give extra time after the encrypted body lands for any deferred handlers.
                await Task.Delay(TimeSpan.FromSeconds(PostCaptureWaitSeconds));

                // Probe A2 — apply namespace-narrowed wrap AFTER bundle loads. Stronger
                // signal than the global A1 hook because it filters by namespace.
                var probeA2Snapshot = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const out = { wraps: [], errors: [], allKeysSurvey: null };
  try {
    // Survey: what's actually on globalThis after the bundle loads?
    const allKeys = Object.keys(globalThis);
    const vmtKeys = allKeys.filter(k => k.startsWith('vmt_') || k.startsWith('vmf_') || k.startsWith('vm'));
    const customGlobals = allKeys.filter(k => /^[a-z][a-z]_[0-9]/.test(k));
    const shortIdentifiers = allKeys.filter(k => /^[A-Z][a-z]?$/.test(k) || /^[A-Z]i$/.test(k));
    const obfuscatedNames = allKeys.filter(k => /^[A-Za-z]{1,3}$/.test(k));
    out.allKeysSurvey = {
      totalCount: allKeys.length,
      vmtKeys, customGlobals, shortIdentifiers: shortIdentifiers.slice(0, 50),
      obfuscatedNamesCount: obfuscatedNames.length,
      sampleObfuscated: obfuscatedNames.slice(0, 50),
    };

    const nsKey = allKeys.find(k => k.startsWith('vmt_') || k.startsWith('vmf_'));
    if (!nsKey) {
      out.errors.push('no vmt_* namespace on globalThis — bundle may load it lazily or asynchronously');
      return JSON.stringify(out);
    }
    out.nsKey = nsKey;
    const ns = globalThis[nsKey];

    const fnSnapshot = {};
    for (const k of Object.keys(ns)) {
      try {
        const v = ns[k];
        if (typeof v === 'function') {
          fnSnapshot[k] = {
            length: v.length,
            name: v.name || null,
            srcPreview: v.toString().slice(0, 250),
          };
        } else if (v && typeof v === 'object') {
          fnSnapshot[k] = { objKeys: Object.keys(v).slice(0, 20), type: 'object' };
        } else {
          fnSnapshot[k] = { type: typeof v };
        }
      } catch (e) { fnSnapshot[k] = { error: String(e).slice(0, 100) }; }
    }
    out.namespaceSnapshot = fnSnapshot;

  } catch (e) {
    out.errors.push(String(e).slice(0, 200));
  }
  return JSON.stringify(out);
})()
");

                // Dump the response reads captured by Probe B.
                var responseReads = await page.EvaluateExpressionAsync<string>(@"
JSON.stringify({
  responseReadCount: window.__comixProbe.responseReads.length,
  responseReads: window.__comixProbe.responseReads,
  plaintextHitCount: window.__comixProbe.plaintextHits.length,
  plaintextHits: window.__comixProbe.plaintextHits,
})
");

                // Probe F — score every function on the namespace by decryption-oracle
                // signature (atob, JSON.parse, Uint8Array, charCodeAt).
                var srcInspection = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const out = { inspected: 0, candidates: [], rawNamespaceList: null };
  try {
    const allKeys = Object.keys(globalThis);
    const nsKey = allKeys.find(k => k.startsWith('vmt_') || k.startsWith('vmf_'));
    if (!nsKey) {
      // Fallback: also inspect top-level short-identifier fns (Mr, Ji, Ti, etc.)
      const wellKnown = allKeys.filter(k => /^[A-Z][a-z]?$/.test(k) || /^[A-Za-z]{1,3}$/.test(k));
      out.rawNamespaceList = wellKnown.slice(0, 50);
      for (const n of wellKnown) {
        try {
          const v = globalThis[n];
          if (typeof v !== 'function') continue;
          out.inspected++;
          const src = v.toString();
          const signals = {
            hasAtob: src.includes('atob'),
            hasCharCodeAt: src.includes('charCodeAt'),
            hasFromCharCode: src.includes('fromCharCode'),
            hasJsonParse: src.includes('JSON.parse'),
            hasUint8: src.includes('Uint8Array'),
            hasXor: /[a-z]\s*\^|XOR/.test(src),
            hasCrypto: src.includes('crypto') || src.includes('subtle'),
            len: src.length,
          };
          const score = (signals.hasAtob ? 2 : 0) + (signals.hasJsonParse ? 2 : 0)
                      + ((signals.hasCharCodeAt || signals.hasFromCharCode) ? 1 : 0)
                      + (signals.hasUint8 ? 1 : 0) + (signals.hasXor ? 1 : 0)
                      + (signals.hasCrypto ? 2 : 0);
          if (score >= 1) {
            out.candidates.push({
              source: 'global', name: n, fnLength: v.length, signals, score,
              srcPreview: src.slice(0, 300),
            });
          }
        } catch (_) {}
      }
      out.candidates.sort((a,b) => b.score - a.score);
      return JSON.stringify(out);
    }

    const ns = globalThis[nsKey];
    for (const k of Object.keys(ns)) {
      try {
        const v = ns[k];
        if (typeof v !== 'function') continue;
        out.inspected++;
        const src = v.toString();
        const signals = {
          hasAtob: src.includes('atob'),
          hasCharCodeAt: src.includes('charCodeAt'),
          hasFromCharCode: src.includes('fromCharCode'),
          hasJsonParse: src.includes('JSON.parse'),
          hasUint8: src.includes('Uint8Array'),
          hasXor: /[a-z]\s*\^|XOR/.test(src),
          hasCrypto: src.includes('crypto') || src.includes('subtle'),
          len: src.length,
        };
        const score = (signals.hasAtob ? 2 : 0) + (signals.hasJsonParse ? 2 : 0)
                    + ((signals.hasCharCodeAt || signals.hasFromCharCode) ? 1 : 0)
                    + (signals.hasUint8 ? 1 : 0) + (signals.hasXor ? 1 : 0)
                    + (signals.hasCrypto ? 2 : 0);
        if (score >= 1) {
          out.candidates.push({
            source: 'ns', name: k, fnLength: v.length, signals, score,
            srcPreview: src.slice(0, 300),
          });
        }
      } catch (_) {}
    }
    out.candidates.sort((a,b) => b.score - a.score);
  } catch (e) { out.error = String(e).slice(0, 200); }
  return JSON.stringify(out);
})()
");

                // Probe G — invoke top candidates with multiple arg shapes against the
                // captured encrypted body.
                string targetedFuzz = null;
                if (!string.IsNullOrEmpty(browserOwnEncrypted))
                {
                    var encryptedJs = browserOwnEncrypted
                        .Replace("\\", "\\\\")
                        .Replace("'", "\\'")
                        .Replace("\n", "\\n")
                        .Replace("\r", string.Empty);
                    targetedFuzz = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const fullEnv = '" + encryptedJs + @"';
  let eValue;
  try {
    const parsed = JSON.parse(fullEnv);
    eValue = parsed.e;
  } catch (e) { return JSON.stringify({error: 'parse failed', err: String(e)}); }

  let cfg = null;
  try {
    const meta = document.querySelector('meta[name=""cfg""]');
    if (meta) cfg = meta.getAttribute('content');
  } catch (_) {}

  const candidates = [];
  const tryFn = (label, fn, args, argShape) => {
    if (typeof fn !== 'function') return;
    try {
      const out = fn(...args);
      const isStr = typeof out === 'string';
      const isObj = out && typeof out === 'object';
      const outStr = isStr ? out : (isObj ? JSON.stringify(out) : String(out));
      if (outStr && outStr.length > 50
          && (outStr.includes('items') || outStr.includes('result')
              || outStr.includes('chapter') || outStr.includes('pages')
              || (isStr && (outStr.trim().startsWith('{') || outStr.trim().startsWith('['))))) {
        candidates.push({
          label, argShape, outType: typeof out, outLen: outStr.length,
          sample: outStr.slice(0, 400),
        });
      }
    } catch (_) {}
  };

  const allKeys = Object.keys(globalThis);
  const nsKey = allKeys.find(k => k.startsWith('vmt_') || k.startsWith('vmf_'));
  if (nsKey) {
    const ns = globalThis[nsKey];
    for (const k of Object.keys(ns)) {
      const fn = ns[k];
      if (typeof fn !== 'function') continue;
      tryFn('ns.' + k, fn, [eValue], 'e-value');
      tryFn('ns.' + k, fn, [fullEnv], 'full-env-str');
      tryFn('ns.' + k, fn, [{ e: eValue }], 'env-obj');
      tryFn('ns.' + k, fn, [{ data: { e: eValue } }], 'response-data-shape');
      if (cfg) {
        tryFn('ns.' + k, fn, [eValue, cfg], 'e-value+cfg');
        tryFn('ns.' + k, fn, [cfg, eValue], 'cfg+e-value');
      }
    }
  }

  // Also try every short-identifier top-level fn.
  const shortGlobals = allKeys.filter(k => /^[A-Z][a-z]?$/.test(k) || /^[A-Za-z]{1,3}$/.test(k));
  for (const n of shortGlobals) {
    const fn = globalThis[n];
    if (typeof fn !== 'function') continue;
    tryFn('global.' + n, fn, [eValue], 'e-value');
    tryFn('global.' + n, fn, [fullEnv], 'full-env-str');
    tryFn('global.' + n, fn, [{ e: eValue }], 'env-obj');
    if (cfg) {
      tryFn('global.' + n, fn, [eValue, cfg], 'e-value+cfg');
    }
  }

  return JSON.stringify({
    cfgPresent: cfg != null,
    cfgPreview: cfg ? cfg.slice(0, 200) : null,
    eValuePreview: eValue ? eValue.slice(0, 100) : null,
    eValueLen: eValue ? eValue.length : null,
    candidates,
  });
})()
");
                }

                // Probe E — React fiber state on rendered chapter rows.
                var reactProbe = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const out = { rowCount: 0, sampleProps: null };
  try {
    const rows = document.querySelectorAll('.mchap-row, [class*=""mchap""], [class*=""chapter""]');
    out.rowCount = rows.length;
    out.firstRowClass = rows.length > 0 ? rows[0].className : null;
    if (rows.length > 0) {
      const row = rows[0];
      const keys = Object.keys(row).filter(k => k.startsWith('__react'));
      out.fiberKeys = keys;
      const propsKey = keys.find(k => k.startsWith('__reactProps'));
      if (propsKey) {
        try {
          out.sampleProps = JSON.stringify(row[propsKey]).slice(0, 600);
        } catch (e) { out.sampleProps = 'serialize error: ' + e.message; }
      }
    }
  } catch (e) { out.error = String(e).slice(0, 200); }
  return JSON.stringify(out);
})()
");

                // Probe H — check loaded scripts to identify the secure chunk + bundle paths.
                var loadedScripts = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const scripts = Array.from(document.scripts);
  return JSON.stringify({
    count: scripts.length,
    sources: scripts
      .map(s => s.src || '<inline>')
      .filter(s => s.indexOf('comix.to') >= 0 || s === '<inline>')
      .slice(0, 30)
  });
})()
");

                var result = new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
                    ["CapturedToken"] = capturedToken != null
                        ? (capturedToken.Length > 50 ? capturedToken[..50] + "..." : capturedToken)
                        : null,
                    ["BrowserOwnStatus"] = browserOwnStatus,
                    ["BrowserOwnBodyLen"] = browserOwnEncrypted?.Length,
                    ["BrowserOwnBodyPreview"] = browserOwnEncrypted != null
                        ? (browserOwnEncrypted.Length > 200 ? browserOwnEncrypted[..200] : browserOwnEncrypted)
                        : null,
                    ["ProbeB_responseReads"] = responseReads,
                    ["ProbeA2_snapshot"] = probeA2Snapshot,
                    ["ProbeG_targetedFuzz"] = targetedFuzz,
                    ["ProbeF_srcInspection"] = srcInspection,
                    ["ProbeE_reactProbe"] = reactProbe,
                    ["ProbeH_loadedScripts"] = loadedScripts,
                };

                SaveResult(result);

                TestContext.WriteLine("================ DEEP ORACLE PROBE RESULT ================");
                TestContext.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("==========================================================");
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
                var file = Path.Combine(dir, $"oracle-deep-probe-{ts}.json");
                File.WriteAllText(file, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("Deep oracle probe result saved to: " + file);
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
