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
    /// 2026-05-23 — Investigation Phase 3, iteration 3 — VM-dispatcher hook probe.
    ///
    /// <para>
    /// Iter 2 revealed the bundle uses VMP (Virtual Machine Protection) obfuscation —
    /// every function on <c>globalThis.vmt_95379</c> delegates to
    /// <c>vmX_33ffab(&lt;opcode&gt;, arguments, ...)</c> with an integer opcode. The
    /// VM dispatcher is what actually does the work; the namespace fns are thin
    /// trampolines. Fuzzing the trampolines returned the input echoed back unchanged
    /// (the VM opcodes for those fns don't accept encrypted-body input).
    /// </para>
    ///
    /// <para>
    /// Strategy: hook <c>vmX_33ffab</c> itself BEFORE bundle loads via
    /// <c>EvaluateExpressionOnNewDocumentAsync</c>, log every invocation where any arg
    /// (positional or arguments object element) is the encrypted envelope (long
    /// base64-shaped string starting with the known prefix). The opcode + originating
    /// stack frame tells us WHICH virtual function handles decryption.
    /// </para>
    ///
    /// <para>
    /// Additionally we hook into the page's data-fetching pipeline by hooking axios
    /// directly if reachable (or its successor): if the bundle ships axios via the VM,
    /// we can find the response interceptor that decrypts.
    /// </para>
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("VM-dispatcher hook probe — runs only when explicitly invoked")]
    public class ComixOracleVmProbeFixture
    {
        private const string ComixBase = "https://comix.to";
        private const string TargetHid = "mr3m0";
        private const int CaptureTimeoutSeconds = 30;
        private const int PostCaptureWaitSeconds = 15;

        [Test]
        public async Task Discover_oracle_via_vm_dispatcher_hook()
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

            // Install a Proxy-based interceptor on globalThis BEFORE the bundle loads,
            // so when the VM dispatcher fn (vmX_33ffab) gets defined we can wrap it.
            //
            // Strategy: define a getter/setter pair on globalThis.vmX_33ffab that
            // captures the first assignment and replaces it with a wrapped version.
            const string vmHookInstall = @"
(function() {
  if (window.__comixVm) return;
  const state = {
    vmFnName: null,
    vmCallCount: 0,
    encryptedInputCalls: [],   // calls where any arg has the {e:...} envelope or long b64
    decryptedOutputCalls: [],  // calls whose return value is plaintext JSON
    sampleCalls: [],           // small sample of all calls for context
  };
  window.__comixVm = state;

  // Heuristics.
  const looksEncryptedEnv = (s) => typeof s === 'string' && s.length > 200
      && (s.indexOf('""e"":""') >= 0 || /^[A-Za-z0-9+/=_-]{200,}/.test(s));
  const looksLongB64 = (s) => typeof s === 'string' && s.length > 200
      && /^[A-Za-z0-9+/=_-]+$/.test(s.slice(0, 200));
  const looksPlaintext = (v) => {
    if (typeof v === 'string') {
      const t = v.trim();
      return t.length > 50 && (t[0] === '{' || t[0] === '[') && t.indexOf('""e"":""') !== 0;
    }
    if (v && typeof v === 'object') {
      try {
        const keys = Object.keys(v);
        if (keys.length === 0) return false;
        return keys.some(k => ['result','data','items','pages','chapters','status'].includes(k));
      } catch (_) { return false; }
    }
    return false;
  };

  const summarizeArg = (a) => {
    if (a == null) return { type: typeof a };
    if (typeof a === 'string') {
      return { type: 'string', len: a.length, preview: a.slice(0, 60) };
    }
    if (typeof a === 'number') return { type: 'number', value: a };
    if (typeof a === 'object') {
      try {
        const ser = JSON.stringify(a);
        return { type: 'object', len: ser.length, preview: ser.slice(0, 120) };
      } catch (_) { return { type: 'object', error: true }; }
    }
    return { type: typeof a };
  };

  const summarizeOut = (o) => {
    if (o == null) return { type: typeof o };
    if (typeof o === 'string') return { type: 'string', len: o.length, preview: o.slice(0, 200) };
    if (typeof o === 'object') {
      try {
        const ser = JSON.stringify(o);
        return { type: 'object', len: ser.length, preview: ser.slice(0, 300) };
      } catch (_) { return { type: 'object', error: true }; }
    }
    return { type: typeof o };
  };

  // Heuristic candidate names for the VM dispatcher. The iter 2 snapshot revealed
  // 'vmX_33ffab' — but the hash suffix may rotate per deploy. Also try Pn (saw in Ci
  // function: `Pn(PX, arguments, Pt, Pf, PG, this)`).
  // We define a Proxy on globalThis that traps the assignment of any vmX_* identifier.
  const wrapDispatcher = (origFn, fnName) => {
    return function wrappedVm(...args) {
      state.vmCallCount++;
      let out;
      let outErr = null;
      try {
        out = origFn.apply(this, args);
      } catch (e) { outErr = e; throw e; }
      finally {
        try {
          let hasEncrypted = false;
          for (const a of args) {
            if (looksEncryptedEnv(a) || looksLongB64(a)) { hasEncrypted = true; break; }
            if (a && typeof a === 'object') {
              // arguments-like object
              try {
                for (const v of (Array.isArray(a) ? a : Object.values(a))) {
                  if (looksEncryptedEnv(v) || looksLongB64(v)) { hasEncrypted = true; break; }
                  if (v && typeof v === 'object' && typeof v.e === 'string'
                      && (looksEncryptedEnv(v.e) || looksLongB64(v.e))) {
                    hasEncrypted = true; break;
                  }
                }
              } catch (_) {}
              if (hasEncrypted) break;
            }
          }
          const outPlain = looksPlaintext(out);
          if (hasEncrypted && state.encryptedInputCalls.length < 30) {
            state.encryptedInputCalls.push({
              fn: fnName,
              opcode: typeof args[0] === 'number' ? args[0] : null,
              argSummaries: args.slice(0, 6).map(summarizeArg),
              out: summarizeOut(out),
              outPlain,
              stack: new Error().stack.split('\n').slice(1, 6).join(' | '),
            });
          }
          if (outPlain && state.decryptedOutputCalls.length < 30) {
            // Even more important — record calls whose OUTPUT is plaintext-shaped.
            state.decryptedOutputCalls.push({
              fn: fnName,
              opcode: typeof args[0] === 'number' ? args[0] : null,
              argSummaries: args.slice(0, 6).map(summarizeArg),
              out: summarizeOut(out),
              stack: new Error().stack.split('\n').slice(1, 6).join(' | '),
            });
          }
          if (state.sampleCalls.length < 20 && state.vmCallCount % 100 === 0) {
            state.sampleCalls.push({
              fn: fnName,
              opcode: typeof args[0] === 'number' ? args[0] : null,
              argCount: args.length,
              outType: typeof out,
            });
          }
        } catch (_) {}
      }
      return out;
    };
  };

  // Intercept vmX_* and Pn name patterns. The bundle assigns these as top-level
  // declarations early in the IIFE. We watch via Proxy + property defineProperty.
  // Simpler approach: poll globalThis for the appearance of any vmX_* fn shortly
  // after page load. Since this is post-load polling not pre-load wrap, we MUST
  // be careful that the wrap happens BEFORE the bundle starts calling the fn for
  // the encryption request. The chapter API request fires ~3-7s after page DCL;
  // we poll every 50ms starting from t=0 of the bundle loading.

  // Use a Proxy over globalThis property defines via Object.defineProperty trap.
  const trapNames = (predicate, makeWrap) => {
    // Snapshot existing globals first.
    const installed = new Set();
    const sweep = () => {
      try {
        for (const k of Object.getOwnPropertyNames(globalThis)) {
          if (installed.has(k)) continue;
          if (!predicate(k)) continue;
          const v = globalThis[k];
          if (typeof v !== 'function') continue;
          installed.add(k);
          try {
            globalThis[k] = makeWrap(v, k);
          } catch (_) {}
        }
      } catch (_) {}
    };
    sweep();
    const iv = setInterval(sweep, 25);
    setTimeout(() => { try { clearInterval(iv); } catch (_) {} }, 30000);
  };

  trapNames(
    (k) => /^vmX_[a-zA-Z0-9]+$/.test(k) || k === 'Pn',
    wrapDispatcher
  );

  // Also instrument XHR.responseText AND any post-response handlers — record what
  // happens after the encrypted body lands.
  try {
    const xhrProto = XMLHttpRequest.prototype;
    const respTextDesc = Object.getOwnPropertyDescriptor(xhrProto, 'responseText');
    if (respTextDesc && respTextDesc.get) {
      const origGet = respTextDesc.get;
      Object.defineProperty(xhrProto, 'responseText', {
        configurable: true,
        get: function() {
          const v = origGet.call(this);
          state.lastXhrUrl = this.responseURL;
          state.lastXhrLen = typeof v === 'string' ? v.length : null;
          return v;
        }
      });
    }
  } catch (_) {}
})();
";

            await page.EvaluateExpressionOnNewDocumentAsync(vmHookInstall);

            // Track the captured token + encrypted body for cross-reference.
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

                // Dump the VM call state.
                var vmState = await page.EvaluateExpressionAsync<string>(@"
JSON.stringify({
  vmCallCount: window.__comixVm.vmCallCount,
  encryptedInputCalls: window.__comixVm.encryptedInputCalls,
  decryptedOutputCalls: window.__comixVm.decryptedOutputCalls,
  sampleCalls: window.__comixVm.sampleCalls,
  lastXhrUrl: window.__comixVm.lastXhrUrl,
  lastXhrLen: window.__comixVm.lastXhrLen,
})
");

                // Dump all currently-defined vmX_* / Pn globals.
                var globalsList = await page.EvaluateExpressionAsync<string>(@"
JSON.stringify(Object.getOwnPropertyNames(globalThis).filter(k => /^(vmX_|Pn|Pl|Pk|Pi|Po|Pp|Pq|Pn|Pm)/.test(k) || /^vm[a-zA-Z]_/.test(k)))
");

                // Now also try a direct decrypt attempt — call vmX_33ffab with the
                // captured encrypted envelope at every opcode 0..300 to see which
                // opcode returns plaintext.
                string opcodeFuzz = null;
                if (!string.IsNullOrEmpty(browserOwnEncrypted))
                {
                    var encryptedJs = browserOwnEncrypted
                        .Replace("\\", "\\\\")
                        .Replace("'", "\\'")
                        .Replace("\n", "\\n")
                        .Replace("\r", string.Empty);
                    opcodeFuzz = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const fullEnv = '" + encryptedJs + @"';
  let eValue;
  try { eValue = JSON.parse(fullEnv).e; } catch (_) { return JSON.stringify({err:'parse'}); }

  // Find the VM dispatcher by name.
  const vmKey = Object.getOwnPropertyNames(globalThis).find(k => /^vmX_/.test(k));
  if (!vmKey) return JSON.stringify({err:'no vmX_ found'});
  const vm = globalThis[vmKey];

  // Search whole-namespace fns for one whose VM opcode produces plaintext.
  const nsKey = Object.keys(globalThis).find(k => k.startsWith('vmt_'));
  if (!nsKey) return JSON.stringify({err:'no namespace'});
  const ns = globalThis[nsKey];

  // Extract opcodes from each fn's toString() — they're VM trampolines:
  //   `function Zi(P){return vmX_33ffab(213,arguments,void 0,void 0,new.target,this)}`
  const opcodes = [];
  for (const k of Object.keys(ns)) {
    const v = ns[k];
    if (typeof v !== 'function') continue;
    const m = v.toString().match(/vmX_[a-zA-Z0-9]+\((\d+)/);
    if (m) opcodes.push({ fn: k, opcode: parseInt(m[1], 10) });
  }

  // Try invoking the namespace fn directly (which calls vmX with the opcode) using
  // every plausible input shape.
  const candidates = [];
  const tryShape = (name, fn, args, shape) => {
    try {
      const out = fn.apply(null, args);
      const isStr = typeof out === 'string';
      const isObj = out && typeof out === 'object';
      const outStr = isStr ? out : (isObj ? JSON.stringify(out) : String(out));
      const isPlain = outStr && outStr.length > 50
          && (outStr.indexOf('items') >= 0 || outStr.indexOf('result') >= 0
              || outStr.indexOf('chapter') >= 0 || outStr.indexOf('pages') >= 0)
          && outStr.indexOf('""e"":""') !== 0;
      if (isPlain) {
        candidates.push({ name, shape, outType: typeof out, outLen: outStr.length, sample: outStr.slice(0, 400) });
      }
    } catch (_) {}
  };

  for (const { fn, opcode } of opcodes) {
    const v = ns[fn];
    tryShape(`ns.${fn}(op=${opcode})`, v, [eValue], 'e-value');
    tryShape(`ns.${fn}(op=${opcode})`, v, [fullEnv], 'full-env-str');
    tryShape(`ns.${fn}(op=${opcode})`, v, [{ e: eValue }], 'env-obj');
  }

  // Also try invoking the VM dispatcher DIRECTLY with each opcode + the encrypted
  // body packed as arguments. vmX signature: (opcode, arguments, vmTable, ?, newTarget, thisArg).
  // We synthesize an `arguments`-like array.
  for (let op = 0; op < 250; op++) {
    try {
      const out = vm(op, [eValue], void 0, void 0, void 0, null);
      const isStr = typeof out === 'string';
      const isObj = out && typeof out === 'object';
      const outStr = isStr ? out : (isObj ? JSON.stringify(out) : String(out));
      if (outStr && outStr.length > 50
          && (outStr.indexOf('items') >= 0 || outStr.indexOf('result') >= 0 || outStr.indexOf('chapter') >= 0)
          && outStr.indexOf('""e"":""') !== 0) {
        candidates.push({ name: `vm-direct(op=${op})`, shape: 'direct-evalue', outType: typeof out, outLen: outStr.length, sample: outStr.slice(0, 400) });
      }
    } catch (_) {}
    try {
      const out = vm(op, [fullEnv], void 0, void 0, void 0, null);
      const isStr = typeof out === 'string';
      const isObj = out && typeof out === 'object';
      const outStr = isStr ? out : (isObj ? JSON.stringify(out) : String(out));
      if (outStr && outStr.length > 50
          && (outStr.indexOf('items') >= 0 || outStr.indexOf('result') >= 0 || outStr.indexOf('chapter') >= 0)
          && outStr.indexOf('""e"":""') !== 0) {
        candidates.push({ name: `vm-direct(op=${op})`, shape: 'direct-fullenv', outType: typeof out, outLen: outStr.length, sample: outStr.slice(0, 400) });
      }
    } catch (_) {}
  }

  return JSON.stringify({ opcodeMap: opcodes, candidates, vmKey });
})()
");
                }

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
                    ["VmState"] = vmState,
                    ["GlobalsList"] = globalsList,
                    ["OpcodeFuzz"] = opcodeFuzz,
                };

                SaveResult(result);

                TestContext.WriteLine("================ VM ORACLE PROBE RESULT ================");
                TestContext.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("========================================================");
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
                var file = Path.Combine(dir, $"oracle-vm-probe-{ts}.json");
                File.WriteAllText(file, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("VM oracle probe result saved to: " + file);
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
