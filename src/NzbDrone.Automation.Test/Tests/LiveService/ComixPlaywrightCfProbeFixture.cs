using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.LiveService;

/// <summary>
/// TEMPORARY Phase 33.3 front-1 de-risk probe (NOT a permanent fixture — delete after the
/// PuppeteerSharp→Playwright signer port decision is locked). Empirically answers two
/// questions before committing to the signer re-architecture:
///   1. Does a STANDARD Playwright headless Chromium (the exact launch the repo's
///      PlaywrightSetUpFixture uses — Headless=true, no assistantMode/stealth args) cold-solve
///      comix.to's Cloudflare managed challenge? (PuppeteerSharp with Playwright's EXACT args
///      could NOT — pageTitle stuck on "Just a moment..." after 45s — confirming the
///      discriminator is the CDP Runtime.enable leak, not launch flags.)
///   2. If it clears, does the solved front-2 oracle (manga-* bundle export `.get(apiPath)`)
///      return decrypted JSON through Playwright? Probes ALL module exports for the working
///      client (validates the "structural export discovery" hardening for the real signer).
///
/// [Explicit] + non-AutomationTest base so it never runs in normal suites and does NOT boot
/// NzbDroneRunner. Run manually via dotnet test with a FullyQualifiedName filter on
/// ComixPlaywrightCfProbe, redirecting output to a file (Chromium child holds stdout).
/// </summary>
[TestFixture]
[Explicit("Phase 33.3 front-1 live de-risk probe; hits live comix.to + Cloudflare")]
[Category("LiveComixProbe")]
public class ComixPlaywrightCfProbeFixture
{
    private IPlaywright _playwright;
    private IBrowser _browser;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        // Headless mirrors PlaywrightSetUpFixture.cs:54; MANGARR_PW_HEADED=1 flips to headed to
        // test the two-condition CF model (headed + Playwright-driving). MANGARR_PW_CHROME=1 uses
        // the real Google Chrome channel (vs bundled Chromium) to isolate the codec/branding axis.
        var headed = Environment.GetEnvironmentVariable("MANGARR_PW_HEADED") == "1";
        var useChrome = Environment.GetEnvironmentVariable("MANGARR_PW_CHROME") == "1";
        var launchOptions = new BrowserTypeLaunchOptions { Headless = !headed };
        if (useChrome)
        {
            launchOptions.Channel = "chrome";
        }

        TestContext.Progress.WriteLine($"[PROBE LAUNCH] headed={headed} channel={(useChrome ? "chrome" : "chromium")}");
        _browser = await _playwright.Chromium.LaunchAsync(launchOptions);
    }

    [OneTimeTearDown]
    public async Task TearDownAsync()
    {
        if (_browser != null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }

    [Test]
    public async Task Headless_Playwright_clears_cf_and_oracle_returns_decrypted_json()
    {
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
        });
        var page = await context.NewPageAsync();

        // Sniff the manga-* bundle URL (structural prefix; token suffix rotates per deploy).
        string mangaBundleUrl = null;
        page.Response += (_, response) =>
        {
            var url = response.Url;
            if (url != null
                && url.Contains("comix.to", StringComparison.OrdinalIgnoreCase)
                && url.Contains("/dist/manga-", StringComparison.OrdinalIgnoreCase)
                && url.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                mangaBundleUrl = url;
            }
        };

        await page.GotoAsync("https://comix.to/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000,
        });

        // RDP-confound diagnostic: capture the REAL window dims + webdriver on the (likely
        // still-challenged) page. outer==0 => headed did not truly render (Windows-RDP session
        // confound); outer!=0 but still blocked => the driving layer (not the display) is the tell.
        var earlyDiag = await page.EvaluateAsync<string>(
            @"() => JSON.stringify({
                inner:[window.innerWidth,window.innerHeight],
                outer:[window.outerWidth,window.outerHeight],
                webdriver: navigator.webdriver,
                ua: navigator.userAgent,
                title: document.title
            })");
        await System.IO.File.WriteAllTextAsync(@"C:\tmp\pw-probe-diag.txt", earlyDiag);

        // ── FRONT 1: poll the title until the CF interstitial clears (or 40s). ──
        var sw = Stopwatch.StartNew();
        var title = await page.TitleAsync();
        while ((string.IsNullOrEmpty(title) || title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase))
               && sw.Elapsed < TimeSpan.FromSeconds(40))
        {
            await Task.Delay(1000);
            title = await page.TitleAsync();
        }

        TestContext.Progress.WriteLine($"[CF PROBE] cleared={!title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)} " +
                                       $"elapsed={sw.ElapsedMilliseconds}ms title='{title}' url='{page.Url}'");

        title.Should().NotContain("Just a moment",
            "FRONT 1: standard headless Playwright must cold-solve comix.to's Cloudflare managed challenge " +
            "(PuppeteerSharp with identical flags could not — this is the existence proof gating the signer port).");

        // ── FRONT 2: confirm the manga-* bundle oracle returns decrypted JSON. ──
        // Give the SPA a beat to finish importing its module graph after the challenge reload.
        await Task.Delay(2000);

        mangaBundleUrl.Should().NotBeNullOrEmpty(
            "FRONT 2: the manga-* bundle must load after CF clears (oracle source).");
        TestContext.Progress.WriteLine($"[ORACLE PROBE] mangaBundleUrl='{mangaBundleUrl}'");

        // Probe every export for a client whose .get(apiPath) returns a decrypted {items:[...]} body.
        var oracleResult = await page.EvaluateAsync<string>(
            @"async (modUrl) => {
                const mod = await import(modUrl);
                const hits = {};
                for (const k of Object.keys(mod)) {
                  try {
                    const v = mod[k];
                    if (v && typeof v.get === 'function') {
                      const res = await v.get('/manga?keyword=Komi&limit=3');
                      const s = typeof res === 'string' ? res : JSON.stringify(res);
                      if (s && s.includes('items')) { hits[k] = s.slice(0, 160); }
                    }
                  } catch (e) { /* not an http client export */ }
                }
                return JSON.stringify(hits);
              }",
            mangaBundleUrl);

        TestContext.Progress.WriteLine($"[ORACLE PROBE] working exports + sample: {oracleResult}");

        oracleResult.Should().Contain("items",
            "FRONT 2: at least one manga-* bundle export must expose a .get(apiPath) client that returns " +
            "decrypted JSON containing an items array (the env-module oracle through Playwright).");

        await context.CloseAsync();
    }
}
