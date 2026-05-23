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
    /// 2026-05-23 — Investigation Phase 3, iteration 6 — main bundle source extraction.
    ///
    /// <para>
    /// Iter 5 extracted the SECURE bundle and found it's a VMP-protected bytecode loader.
    /// The JSON.parse + decryption logic in it (P4 fn) is for the VM's own self-decryption,
    /// not the API response decryption. The actual API decryption likely lives in the MAIN
    /// or VENDOR bundle (unprotected). Let's fetch those + search for the `.e` access
    /// pattern that handles the encrypted envelope.
    /// </para>
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("main bundle source extraction probe — runs only when explicitly invoked")]
    public class ComixOracleMainBundleProbeFixture
    {
        private const string ComixBase = "https://comix.to";
        private const string TargetHid = "mr3m0";

        [Test]
        public async Task Dump_main_and_vendor_bundles_for_decryption_pattern()
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

            var loadedScripts = new List<string>();
            EventHandler<RequestEventArgs> reqFinishedHandler = (s, e) =>
            {
                try
                {
                    var url = e.Request?.Url;
                    if (url != null && url.Contains(".js", StringComparison.OrdinalIgnoreCase)
                        && url.Contains("comix.to", StringComparison.OrdinalIgnoreCase)
                        && !url.Contains("cdn-cgi", StringComparison.OrdinalIgnoreCase))
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

                List<string> snapshot;
                lock (loadedScripts)
                {
                    snapshot = loadedScripts.Distinct().ToList();
                }

                var results = new List<Dictionary<string, object>>();
                foreach (var url in snapshot)
                {
                    if (url.Contains("secure-", StringComparison.OrdinalIgnoreCase))
                    {
                        // Skip — already analyzed in iter 5.
                        continue;
                    }

                    string src = null;
                    try
                    {
                        src = await page.EvaluateFunctionAsync<string>(
                            "async (u) => { const r = await fetch(u); return await r.text(); }",
                            url);
                    }
                    catch (Exception ex)
                    {
                        TestContext.WriteLine($"Failed to fetch {url}: {ex.Message}");
                        continue;
                    }

                    if (src == null)
                    {
                        continue;
                    }

                    SaveBundleSource(src, url);

                    var info = new Dictionary<string, object>
                    {
                        ["url"] = url,
                        ["len"] = src.Length,
                        ["preview"] = src.Length > 200 ? src[..200] : src,
                        ["analysis"] = AnalyzeBundleSource(src),
                    };
                    results.Add(info);
                }

                var result = new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
                    ["BundleCount"] = results.Count,
                    ["Bundles"] = results,
                };

                SaveResult(result);

                TestContext.WriteLine("================ MAIN BUNDLE PROBE RESULT ================");
                TestContext.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("===========================================================");
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

        private static Dictionary<string, object> AnalyzeBundleSource(string src)
        {
            var findings = new Dictionary<string, object>
            {
                ["len"] = src.Length,
                ["atobOccurrences"] = src.Split("atob(").Length - 1,
                ["jsonParseOccurrences"] = src.Split("JSON.parse").Length - 1,
                ["fromCharCodeOccurrences"] = src.Split("fromCharCode").Length - 1,
                ["axiosOccurrences"] = src.Split("axios").Length - 1,
                ["interceptorOccurrences"] = src.Split("interceptor").Length - 1,
                ["decryptOccurrences"] = src.Split("decrypt").Length - 1,
                ["responseOccurrences"] = src.Split(".response").Length - 1,
                ["fetchOccurrences"] = src.Split("fetch(").Length - 1,
                ["subtleOccurrences"] = src.Split("subtle.").Length - 1,
                ["e_dotAccessOccurrences"] = src.Split(".e\"").Length - 1 + src.Split("[\"e\"]").Length - 1,
            };

            // Sample atob contexts.
            var atobIdx = 0;
            var atobContexts = new List<string>();
            while (atobContexts.Count < 5)
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

            // Sample JSON.parse contexts (look for one that's downstream of '.e' access).
            var jpIdx = 0;
            var jpContexts = new List<string>();
            while (jpContexts.Count < 8)
            {
                var idx = src.IndexOf("JSON.parse", jpIdx, StringComparison.Ordinal);
                if (idx == -1)
                {
                    break;
                }

                var start = Math.Max(0, idx - 250);
                var len = Math.Min(src.Length - start, 450);
                jpContexts.Add(src.Substring(start, len));
                jpIdx = idx + 10;
            }

            findings["jsonParseContexts"] = jpContexts;

            // Look for response interceptor patterns common in axios.
            var interceptorIdx = 0;
            var interceptorContexts = new List<string>();
            while (interceptorContexts.Count < 5)
            {
                var idx = src.IndexOf("interceptors", interceptorIdx, StringComparison.Ordinal);
                if (idx == -1)
                {
                    break;
                }

                var start = Math.Max(0, idx - 100);
                var len = Math.Min(src.Length - start, 400);
                interceptorContexts.Add(src.Substring(start, len));
                interceptorIdx = idx + 12;
            }

            findings["interceptorContexts"] = interceptorContexts;

            // Look for "e" property destructuring patterns.
            var eDestructIdx = 0;
            var eDestructContexts = new List<string>();
            while (eDestructContexts.Count < 8)
            {
                var idx = src.IndexOf("{e:", eDestructIdx, StringComparison.Ordinal);
                if (idx == -1)
                {
                    idx = src.IndexOf("{e}", eDestructIdx, StringComparison.Ordinal);
                }

                if (idx == -1)
                {
                    break;
                }

                var start = Math.Max(0, idx - 100);
                var len = Math.Min(src.Length - start, 300);
                eDestructContexts.Add(src.Substring(start, len));
                eDestructIdx = idx + 3;
            }

            findings["eDestructContexts"] = eDestructContexts;

            return findings;
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
                var file = Path.Combine(dir, $"oracle-main-bundle-probe-{ts}.json");
                File.WriteAllText(file, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("Main bundle probe result saved to: " + file);
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
