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
    /// Final probe: extract structured chapter rows from the rendered DOM. The
    /// chapter list IS in the DOM (page renders correctly) but only the link
    /// snippet was captured before. Walk the parent row to get group name + date
    /// + chapter number all together.
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit]
    public class ComixDomFullExtractFixture
    {
        private const string ComixBase = "https://comix.to";
        private const string TargetHid = "mr3m0";

        [Test]
        public async Task Extract_full_chapter_rows_from_dom()
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

                await Task.Delay(TimeSpan.FromSeconds(4));

                var fullExtract = await page.EvaluateExpressionAsync<string>(@"
(() => {
  const anchors = Array.from(document.querySelectorAll('a[href*=""-chapter-""]'));
  return JSON.stringify(anchors.map(a => {
    // Walk up to the containing row.
    let row = a;
    for (let i = 0; i < 5; i++) {
      if (!row.parentElement) break;
      row = row.parentElement;
      if (row.className && (row.className.includes('mchap-row') || row.className.includes('chapter-row') || row.className.includes('row'))) {
        break;
      }
    }
    return {
      href: a.getAttribute('href'),
      ch: a.textContent.trim(),
      rowText: row.textContent ? row.textContent.trim().slice(0, 200) : '',
      rowHTML: row.outerHTML ? row.outerHTML.slice(0, 500) : '',
    };
  }));
})()
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
                var file = Path.Combine(dir, $"dom-full-extract-{ts}.json");
                File.WriteAllText(file, fullExtract);

                TestContext.WriteLine("Saved: " + file);
                TestContext.WriteLine("Length: " + fullExtract.Length);
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
