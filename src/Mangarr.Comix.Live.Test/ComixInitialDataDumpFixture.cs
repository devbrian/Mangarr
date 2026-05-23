using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Test.Common.Categories;
using PuppeteerSharp;

namespace Mangarr.Comix.Live.Test
{
    /// <summary>
    /// 2026-05-23 third probe: dump the COMPLETE initial-data SSR JSON to disk
    /// so we can read what's actually embedded. The previous probe truncated at
    /// 500 chars and showed 28KB total — need to see the full structure to know
    /// if chapters are SSR-embedded or only hydrated via the encrypted API.
    ///
    /// ALSO: since the title page renders 20 chapter rows in the DOM, those came
    /// from somewhere — either SSR-embedded OR client-side after the encrypted
    /// API call was decoded. This probe disambiguates.
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("initial-data dump — runs only when explicitly invoked")]
    public class ComixInitialDataDumpFixture
    {
        private const string ComixBase = "https://comix.to";
        private const string TargetHid = "mr3m0";

        [Test]
        public async Task Dump_initial_data_script_full_contents()
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

                await Task.Delay(TimeSpan.FromSeconds(3));

                var initialDataJson = await page.EvaluateExpressionAsync<string>(
                    "(() => { const el = document.getElementById('initial-data'); return el ? el.textContent : null; })()");

                // Also dump the full rendered chapter rows (text + href) to see if
                // we have a workable extraction path purely from DOM.
                var domChapters = await page.EvaluateExpressionAsync<string>(@"
JSON.stringify(Array.from(document.querySelectorAll('a[href*=""-chapter-""]')).map(a => ({
  href: a.getAttribute('href'),
  text: (a.textContent || '').trim().slice(0, 200),
  ariaLabel: a.getAttribute('aria-label'),
  rect: { /* skip dims */ },
  innerHTML_first200: (a.innerHTML || '').slice(0, 200),
})))
");

                var dir = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    ".planning",
                    "debug",
                    "evidence",
                    "comix-signer-rotation"));
                Directory.CreateDirectory(dir);
                var ts = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
                var initialDataFile = Path.Combine(dir, $"initial-data-{ts}.json");
                var domChaptersFile = Path.Combine(dir, $"dom-chapters-{ts}.json");

                File.WriteAllText(initialDataFile, initialDataJson ?? "null");
                File.WriteAllText(domChaptersFile, domChapters);

                TestContext.WriteLine("Initial data saved to: " + initialDataFile);
                TestContext.WriteLine("DOM chapters saved to: " + domChaptersFile);
                TestContext.WriteLine("initial-data length: " + (initialDataJson?.Length ?? 0));
                TestContext.WriteLine("Has 'chapter' substring: " + (initialDataJson?.Contains("chapter", StringComparison.OrdinalIgnoreCase) ?? false));
                TestContext.WriteLine("Has chapter ID '9573393' (one we saw in DOM): " + (initialDataJson?.Contains("9573393") ?? false));
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
