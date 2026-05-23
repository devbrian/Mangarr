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
    /// 2026-05-23 — Investigation Phase 3, iteration 7 — PROVE the oracle.
    ///
    /// <para>
    /// Iter 6 found the architecture from <c>env-tfgaak-*.js</c>:
    /// the bundle imports `Hi` from secure-tfgaak, creates an axios instance
    /// `ai = t.create(...)`, installs the ok+result unwrap interceptor, then
    /// calls `Hi(ai)` to add a SECOND interceptor that handles `{e:"..."}`
    /// envelope decryption. After that, `b.get(...)` / `ai.get(...)` returns
    /// plaintext via the interceptor chain.
    /// </para>
    ///
    /// <para>
    /// So <c>Hi(ai)</c> installs an interceptor on the axios instance that handles
    /// <c>{e:"..."}</c> envelope decryption automatically. The bundle already calls
    /// <c>Hi(ai)</c> at load. After that, ANY <c>ai.get(...)</c> call returns
    /// plaintext via the interceptor chain.
    /// </para>
    ///
    /// <para>
    /// Strategy: dynamically import the env module from page context, get its
    /// exported <c>p</c> (the <c>ai</c> axios instance) or <c>f</c> (the <c>b</c>
    /// wrapper with .data unwrap), and call <c>.get('/manga/{hid}/chapters')</c>.
    /// If the result is plaintext JSON → ORACLE PROVEN.
    /// </para>
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("oracle PROVE probe — runs only when explicitly invoked")]
    public class ComixOracleProveFixture
    {
        private const string ComixBase = "https://comix.to";
        private const string TargetHid = "mr3m0";

        [Test]
        public async Task Prove_decryption_via_env_module_export()
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

            // Track the env module's URL.
            string envModuleUrl = null;
            EventHandler<RequestEventArgs> reqFinishedHandler = (s, e) =>
            {
                try
                {
                    var url = e.Request?.Url;
                    if (url != null && url.Contains("env-tfgaak-", StringComparison.OrdinalIgnoreCase))
                    {
                        envModuleUrl = url;
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

                // Wait for bundle to load.
                await Task.Delay(TimeSpan.FromSeconds(10));

                if (envModuleUrl == null)
                {
                    Assert.Fail("Could not capture env module URL");
                    return;
                }

                TestContext.WriteLine("env module URL: " + envModuleUrl);

                // Phase 1: dynamic-import the env module + check what exports look like.
                var importProbe = await page.EvaluateFunctionAsync<string>(
                    @"async (url) => {
                        const mod = await import(url);
                        const keys = Object.keys(mod);
                        const summary = {};
                        for (const k of keys) {
                            const v = mod[k];
                            summary[k] = {
                                type: typeof v,
                                isFn: typeof v === 'function',
                                isObj: typeof v === 'object' && v !== null,
                                ownKeys: typeof v === 'object' && v !== null ? Object.keys(v).slice(0, 20) : null,
                            };
                        }
                        return JSON.stringify({ exportKeys: keys, summary });
                    }",
                    envModuleUrl);

                TestContext.WriteLine("Import probe: " + importProbe);

                // Phase 2: pick the b-like wrapper (exported as 'f' per the bundle:
                //   export { f as a, g as c, v as d, b as f, w as h, k as i, ... }
                // 'b as f' means 'b' is exported as 'f'. The user-facing alias is 'f'.
                // Or use 'p' which is 'ai' directly (the axios instance).
                // First, try the 'f' wrapper:
                var oracleAttempt = await page.EvaluateFunctionAsync<string>(
                    @"async (url, hid) => {
                        const mod = await import(url);
                        const b = mod.f;  // 'b as f' export
                        const ai = mod.p;  // 'ai as p' export
                        try {
                            // Try b.get (wrapped — returns .data)
                            const res1 = await b.get('/manga/' + hid + '/chapters', { params: { page: 1, limit: 20, 'order[number]': 'desc' } });
                            return JSON.stringify({
                                via: 'b.get(f)',
                                resType: typeof res1,
                                isArray: Array.isArray(res1),
                                ownKeys: typeof res1 === 'object' && res1 !== null ? Object.keys(res1).slice(0, 20) : null,
                                preview: typeof res1 === 'string' ? res1.slice(0, 300) : JSON.stringify(res1).slice(0, 600),
                            });
                        } catch (e1) {
                            try {
                                // Fall back to ai.get (raw axios)
                                const res2 = await ai.get('/manga/' + hid + '/chapters', { params: { page: 1, limit: 20, 'order[number]': 'desc' } });
                                return JSON.stringify({
                                    via: 'ai.get(p)',
                                    resType: typeof res2,
                                    status: res2.status,
                                    dataType: typeof res2.data,
                                    dataPreview: typeof res2.data === 'string' ? res2.data.slice(0, 300) : JSON.stringify(res2.data).slice(0, 600),
                                });
                            } catch (e2) {
                                return JSON.stringify({
                                    error: 'both failed',
                                    bGetErr: String(e1).slice(0, 300),
                                    aiGetErr: String(e2).slice(0, 300),
                                });
                            }
                        }
                    }",
                    envModuleUrl,
                    TargetHid);

                TestContext.WriteLine("Oracle attempt: " + oracleAttempt);

                // Phase 3: also try the pages endpoint to make sure both routes work.
                string pagesAttempt = null;
                try
                {
                    var firstChapterId = ExtractFirstChapterIdFromOracleResult(oracleAttempt);
                    if (firstChapterId != null)
                    {
                        pagesAttempt = await page.EvaluateFunctionAsync<string>(
                            @"async (url, chapterId) => {
                                const mod = await import(url);
                                const b = mod.f;
                                try {
                                    const res = await b.get('/chapters/' + chapterId);
                                    return JSON.stringify({
                                        via: 'b.get(chapter detail)',
                                        resType: typeof res,
                                        ownKeys: typeof res === 'object' && res !== null ? Object.keys(res).slice(0, 20) : null,
                                        preview: JSON.stringify(res).slice(0, 600),
                                    });
                                } catch (e) {
                                    return JSON.stringify({ error: String(e).slice(0, 300) });
                                }
                            }",
                            envModuleUrl,
                            firstChapterId);
                        TestContext.WriteLine("Pages attempt: " + pagesAttempt);
                    }
                }
                catch (Exception ex)
                {
                    TestContext.WriteLine("Pages attempt failed: " + ex.Message);
                }

                var result = new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
                    ["EnvModuleUrl"] = envModuleUrl,
                    ["ImportProbe"] = importProbe,
                    ["OracleAttempt"] = oracleAttempt,
                    ["PagesAttempt"] = pagesAttempt,
                };

                SaveResult(result);

                TestContext.WriteLine("================ ORACLE PROVE RESULT ================");
                TestContext.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("=====================================================");
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

        private static string ExtractFirstChapterIdFromOracleResult(string oracleAttempt)
        {
            try
            {
                using var doc = JsonDocument.Parse(oracleAttempt);
                if (!doc.RootElement.TryGetProperty("preview", out var preview))
                {
                    return null;
                }

                var previewStr = preview.GetString();
                if (string.IsNullOrEmpty(previewStr))
                {
                    return null;
                }

                // Find first "id":<digits> pattern.
                var idMatch = System.Text.RegularExpressions.Regex.Match(previewStr, @"""id""\s*:\s*(\d+)");
                if (idMatch.Success)
                {
                    return idMatch.Groups[1].Value;
                }
            }
            catch
            {
                // swallow
            }

            return null;
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
                var file = Path.Combine(dir, $"oracle-prove-{ts}.json");
                File.WriteAllText(file, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                TestContext.WriteLine("Oracle prove result saved to: " + file);
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
