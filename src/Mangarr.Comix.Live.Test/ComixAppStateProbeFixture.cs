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
    /// 2026-05-23 second follow-up probe. The decryption-oracle probe ruled out
    /// fetch/XHR/Response monkey-patching — yet the bundle DOES decrypt envelopes.
    /// The decryption happens at the data-layer level (likely inside a Pinia/Vuex/
    /// Redux store action handler) AFTER the bundle's fetch returns the encrypted
    /// body. Rather than reverse-engineer the decryption call, we can simply read
    /// the DECRYPTED state from the page's app store or rendered DOM.
    ///
    /// This probe:
    /// 1. Loads the page + waits for chapters to render.
    /// 2. Inspects __NUXT__ / __INITIAL_STATE__ / Pinia / Vuex / Redux state slots.
    /// 3. Reads the rendered chapter list from the DOM as a fallback.
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("page app-state probe — runs only when explicitly invoked")]
    public class ComixAppStateProbeFixture
    {
        private const string ComixBase = "https://comix.to";
        private const string TargetHid = "mr3m0";

        [Test]
        public async Task Inspect_page_state_for_decrypted_chapter_list()
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

            try
            {
                await page.GoToAsync(
                    $"{ComixBase}/title/{TargetHid}",
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });

                // Give the bundle plenty of time to render chapters.
                await Task.Delay(TimeSpan.FromSeconds(8));

                // Probe A: dump well-known state container globals.
                var probeA = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const slots = ['__NUXT__', '__INITIAL_STATE__', '__PINIA__', '__VUEX_HMR_RUNTIME__', '__VUE_HMR_RUNTIME__', '__REDUX_DEVTOOLS_EXTENSION__', '__APOLLO_CLIENT__', '__APOLLO_STATE__', '__APP__', '__SAPPER__', '__SVELTEKIT_PAYLOAD__', 'app', 'store', '$store', 'pinia', '$pinia'];
  const present = {};
  for (const s of slots) {
    try {
      const v = window[s];
      if (v !== undefined && v !== null) {
        const stringified = JSON.stringify(v, (k, vv) => {
          if (typeof vv === 'function') return '[fn]';
          if (vv instanceof HTMLElement) return '[Element]';
          return vv;
        }, 0);
        present[s] = stringified && stringified.length > 200 ? { len: stringified.length, head: stringified.slice(0, 500) } : (stringified || '[empty]');
      }
    } catch (e) {
      present[s] = '<JSON.stringify threw: ' + e.message + '>';
    }
  }
  return JSON.stringify(present);
})()
");

                // Probe B: dump <script id="initial-data"> body (server-rendered hydration).
                var probeB = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const el = document.getElementById('initial-data');
  if (!el) return JSON.stringify({ found: false });
  const txt = el.textContent || '';
  return JSON.stringify({ found: true, len: txt.length, hasChapters: txt.indexOf('chapter') !== -1, head: txt.slice(0, 1000), tail: txt.slice(-1000) });
})()
");

                // Probe C: query the DOM for visible chapter rows.
                var probeC = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const candidateSelectors = ['[data-chapter]', '.chapter-row', '.chapter-item', '.chapter-card', '[class*=""chapter""]', 'a[href*=""/chapters/""]', 'a[href*=""-chapter-""]'];
  const results = {};
  for (const sel of candidateSelectors) {
    try {
      const nodes = document.querySelectorAll(sel);
      if (nodes.length > 0) {
        results[sel] = { count: nodes.length, sampleText: Array.from(nodes).slice(0, 3).map(n => n.textContent ? n.textContent.trim().slice(0, 100) : '').filter(Boolean), sampleHref: Array.from(nodes).slice(0, 3).map(n => n.getAttribute ? n.getAttribute('href') : '').filter(Boolean) };
      }
    } catch (_) {}
  }
  return JSON.stringify(results);
})()
");

                // Probe D: hook IntersectionObserver-less state — check for Vue/React internals.
                var probeD = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const root = document.getElementById('app-root') || document.querySelector('div');
  if (!root) return JSON.stringify({ found: false });
  const keys = Object.keys(root).filter(k => k.startsWith('__') || k.startsWith('_'));
  const sample = {};
  for (const k of keys.slice(0, 10)) {
    try {
      const v = root[k];
      sample[k] = { type: typeof v, keys: typeof v === 'object' && v !== null ? Object.keys(v).slice(0, 20) : null };
    } catch (_) {}
  }
  return JSON.stringify({ found: true, keys, sample });
})()
");

                var result = new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
                    ["ProbeA_stateSlots"] = probeA,
                    ["ProbeB_initialDataScript"] = probeB,
                    ["ProbeC_domChapterRows"] = probeC,
                    ["ProbeD_appRootInternals"] = probeD,
                };

                SaveResult(result);

                TestContext.WriteLine("================ APP-STATE PROBE RESULT ================");
                TestContext.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("=========================================================");
            }
            finally
            {
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
                var file = Path.Combine(dir, $"app-state-probe-{ts}.json");
                File.WriteAllText(file, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("App-state probe saved to: " + file);
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
