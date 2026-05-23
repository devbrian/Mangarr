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
    /// 2026-05-23 — Investigation Phase 3, iteration 5 — bundle source extraction.
    ///
    /// <para>
    /// Iter 4 exhaustive trampoline call mapped every fn in <c>vmt_95379</c> to its
    /// behavior. Found that none of them directly returns plaintext given the
    /// encrypted body. Decryption is happening somewhere we can't reach by direct
    /// invocation — likely in <c>Mr</c> (axios-like object with no enumerable keys)
    /// or in <c>Di</c>/<c>Ni</c>/<c>B</c> (storage-like).
    /// </para>
    ///
    /// <para>
    /// Strategy: DOWNLOAD the secure bundle source, scan for crypto patterns. Also
    /// inspect <c>Mr</c> via Object.getOwnPropertyNames + getPrototypeOf to find the
    /// hidden axios-like API. Then attempt to invoke <c>Mr.get/post</c> with the
    /// chapter API URL — this might use the bundle's own already-installed response
    /// interceptor and return plaintext.
    /// </para>
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("bundle source extraction probe — runs only when explicitly invoked")]
    public class ComixOracleBundleSourceProbeFixture
    {
        private const string ComixBase = "https://comix.to";
        private const string TargetHid = "mr3m0";
        private const int CaptureTimeoutSeconds = 30;
        private const int PostCaptureWaitSeconds = 10;

        [Test]
        public async Task Dump_bundle_source_and_inspect_hidden_apis()
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

            // Capture all loaded script URLs.
            var loadedScripts = new List<string>();
            EventHandler<RequestEventArgs> reqFinishedHandler = (s, e) =>
            {
                try
                {
                    var url = e.Request?.Url;
                    if (url != null && url.Contains(".js", StringComparison.OrdinalIgnoreCase)
                        && url.Contains("comix.to", StringComparison.OrdinalIgnoreCase))
                    {
                        lock (loadedScripts)
                        {
                            loadedScripts.Add(url);
                        }
                    }
                }
                catch
                {
                    // swallow
                }
            };
            page.RequestFinished += reqFinishedHandler;

            try
            {
                await page.GoToAsync(
                    $"{ComixBase}/title/{TargetHid}",
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });

                await Task.Delay(TimeSpan.FromSeconds(8));

                // Deep inspect Mr, Di, Ni, B and the whole namespace via getOwnPropertyNames + proto walk.
                var deepInspect = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const out = { inspectedObjects: {} };
  const inspect = (label, obj) => {
    if (obj == null) { out.inspectedObjects[label] = { type: typeof obj }; return; }
    const info = { type: typeof obj };
    try {
      info.ownKeys = Object.getOwnPropertyNames(obj);
      info.proto = obj.__proto__ ? Object.getOwnPropertyNames(obj.__proto__) : null;
      info.symbols = Object.getOwnPropertySymbols(obj).map(s => s.toString());
      const keyDetails = {};
      for (const k of info.ownKeys.slice(0, 30)) {
        try {
          const desc = Object.getOwnPropertyDescriptor(obj, k);
          const v = desc.value;
          keyDetails[k] = {
            type: typeof v,
            isFn: typeof v === 'function',
            fnLen: typeof v === 'function' ? v.length : null,
            fnSrcPreview: typeof v === 'function' ? v.toString().slice(0, 200) : null,
            valuePreview: typeof v === 'string' ? v.slice(0, 100) : (typeof v === 'number' ? v : null),
          };
        } catch (e) { keyDetails[k] = { error: String(e).slice(0, 100) }; }
      }
      info.keyDetails = keyDetails;
    } catch (e) { info.error = String(e).slice(0, 200); }
    out.inspectedObjects[label] = info;
  };

  inspect('globalThis.Mr', globalThis.Mr);
  inspect('globalThis.Di', globalThis.Di);
  inspect('globalThis.Ni', globalThis.Ni);
  inspect('globalThis.B', globalThis.B);
  inspect('globalThis.E', globalThis.E);
  inspect('globalThis.Ui', globalThis.Ui);
  inspect('globalThis.vi', globalThis.vi);
  inspect('globalThis.wi', globalThis.wi);

  const nsKey = Object.keys(globalThis).find(k => k.startsWith('vmt_'));
  if (nsKey) {
    inspect('vmt.Mr', globalThis[nsKey].Mr);
    inspect('vmt.Di', globalThis[nsKey].Di);
    inspect('vmt.Ni', globalThis[nsKey].Ni);
    inspect('vmt.B', globalThis[nsKey].B);
    inspect('vmt.E', globalThis[nsKey].E);
    inspect('vmt.Ui', globalThis[nsKey].Ui);
    inspect('vmt.vi', globalThis[nsKey].vi);
    inspect('vmt.wi', globalThis[nsKey].wi);
  }

  return JSON.stringify(out);
})()
");

                // Find the secure bundle script URL.
                string secureBundleUrl = null;
                lock (loadedScripts)
                {
                    secureBundleUrl = loadedScripts.FirstOrDefault(u => u.Contains("secure-", StringComparison.OrdinalIgnoreCase));
                }

                // Fetch the secure bundle source.
                string bundleSource = null;
                if (secureBundleUrl != null)
                {
                    bundleSource = await page.EvaluateFunctionAsync<string>(
                        "async (url) => { const r = await fetch(url); return await r.text(); }",
                        secureBundleUrl);
                }

                // Save the bundle source separately.
                if (bundleSource != null)
                {
                    SaveBundleSource(bundleSource, secureBundleUrl);
                }

                // Search the bundle source for crypto patterns.
                string sourceAnalysis = null;
                if (bundleSource != null)
                {
                    sourceAnalysis = AnalyzeBundleSource(bundleSource);
                }

                var result = new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
                    ["LoadedScriptsCount"] = loadedScripts.Count,
                    ["LoadedScripts"] = loadedScripts,
                    ["SecureBundleUrl"] = secureBundleUrl,
                    ["BundleSourceLen"] = bundleSource?.Length,
                    ["BundleSourcePreview"] = bundleSource != null && bundleSource.Length > 500 ? bundleSource[..500] : bundleSource,
                    ["DeepInspect"] = deepInspect,
                    ["SourceAnalysis"] = sourceAnalysis,
                };

                SaveResult(result);

                TestContext.WriteLine("================ BUNDLE SOURCE PROBE RESULT ================");
                TestContext.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("=============================================================");
            }
            finally
            {
                page.RequestFinished -= reqFinishedHandler;
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

        private static string AnalyzeBundleSource(string src)
        {
            // Search for patterns that suggest decryption logic.
            var findings = new Dictionary<string, object>
            {
                ["totalLen"] = src.Length,
                ["atobOccurrences"] = src.Split("atob(").Length - 1,
                ["jsonParseOccurrences"] = src.Split("JSON.parse").Length - 1,
                ["fromCharCodeOccurrences"] = src.Split("fromCharCode").Length - 1,
                ["charCodeAtOccurrences"] = src.Split("charCodeAt").Length - 1,
                ["uint8Occurrences"] = src.Split("Uint8Array").Length - 1,
                ["subtleOccurrences"] = src.Split("subtle.").Length - 1,
            };

            // Find each atob context.
            var atobIdx = 0;
            var atobContexts = new List<string>();
            while (atobContexts.Count < 10)
            {
                var idx = src.IndexOf("atob(", atobIdx, StringComparison.Ordinal);
                if (idx == -1)
                {
                    break;
                }

                var start = Math.Max(0, idx - 150);
                var len = Math.Min(src.Length - start, 350);
                atobContexts.Add(src.Substring(start, len));
                atobIdx = idx + 5;
            }

            findings["atobContexts"] = atobContexts;

            // Find each JSON.parse context too — the decryption almost certainly ends with JSON.parse.
            var jpIdx = 0;
            var jpContexts = new List<string>();
            while (jpContexts.Count < 10)
            {
                var idx = src.IndexOf("JSON.parse", jpIdx, StringComparison.Ordinal);
                if (idx == -1)
                {
                    break;
                }

                var start = Math.Max(0, idx - 150);
                var len = Math.Min(src.Length - start, 350);
                jpContexts.Add(src.Substring(start, len));
                jpIdx = idx + 10;
            }

            findings["jsonParseContexts"] = jpContexts;

            // Look for the property name '.e' access patterns — that's the encrypted envelope field.
            var dotEContexts = new List<string>();
            var deIdx = 0;
            while (dotEContexts.Count < 10)
            {
                var idx = src.IndexOf(".e\"", deIdx, StringComparison.Ordinal);
                if (idx == -1)
                {
                    idx = src.IndexOf("[\"e\"]", deIdx, StringComparison.Ordinal);
                }

                if (idx == -1)
                {
                    break;
                }

                var start = Math.Max(0, idx - 100);
                var len = Math.Min(src.Length - start, 200);
                dotEContexts.Add(src.Substring(start, len));
                deIdx = idx + 3;
            }

            findings["dotEContexts"] = dotEContexts;

            return JsonSerializer.Serialize(findings, new JsonSerializerOptions { WriteIndented = true });
        }

        private static void SaveBundleSource(string src, string url)
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
                var safe = url.Replace("/", "_").Replace(":", "_").Replace("?", "_");
                if (safe.Length > 60)
                {
                    safe = safe[^60..];
                }

                var file = Path.Combine(dir, $"bundle-source-{ts}-{safe}.js");
                File.WriteAllText(file, src);
                TestContext.WriteLine("Bundle source saved to: " + file);
            }
            catch (Exception ex)
            {
                TestContext.WriteLine("Could not save bundle source: " + ex.Message);
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
                var file = Path.Combine(dir, $"oracle-source-probe-{ts}.json");
                File.WriteAllText(file, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("Bundle source probe result saved to: " + file);
            }
            catch (Exception ex)
            {
                TestContext.WriteLine("Could not save: " + ex.Message);
            }
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
