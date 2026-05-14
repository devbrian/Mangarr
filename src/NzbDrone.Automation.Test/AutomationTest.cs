using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NLog;
using NUnit.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Automation.Test;

[TestFixture]
[AutomationTest]
public abstract class AutomationTest
{
    private NzbDroneRunner _runner;

    protected IBrowserContext Context { get; private set; }
    protected IPage Page { get; private set; }
    protected string RootUri => $"http://localhost:{_runner.Port}";
    protected string ApiKey => _runner.ApiKey;
    protected NzbDroneRunner Runner => _runner;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        // CI-infra F1 fix (ci-test-jobs-latent-faults): PlaywrightSetUpFixture now tolerates a
        // missing Playwright driver/browser (so the unit-tier TestKit fixtures pass in the
        // unit_test job). The automation tier genuinely needs a Browser — fail fast here with a
        // clear message instead of a bare NullReferenceException on Browser.NewContextAsync.
        Assert.That(
            PlaywrightSetUpFixture.BrowserAvailable,
            Is.True,
            "Playwright browser was not provisioned for this job. AutomationTest fixtures must run "
            + "in a job that installs the Playwright driver/browser (automation_test_* jobs), not "
            + "the unit_test job. See PlaywrightSetUpFixture.SetUpAsync.");

        _runner = new NzbDroneRunner(LogManager.GetCurrentClassLogger(), null);
        _runner.KillAll();
        _runner.Start(enableAuth: true);

        // D-07 pre-seed baseline (Plan 18-14 D-C fix): root folder + InProcess
        // download client must exist before the browser opens or the AddManga
        // modal's Add button POST fails its required-field validation
        // (RootFolderPath is mandatory; TranslationProfile/CustomFormatProfile
        // come from Phase 5 baseline migration). Without this seed, every
        // AddMangaFlow.AddByMangaDexIdAsync call times out at ConfirmAddAsync.
        var seedRoot = Path.Combine(_runner.AppData, "MangaLibrary");
        Directory.CreateDirectory(seedRoot);
        await new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, _runner.ApiKey, seedRoot).SeedBaselineAsync();

        Context = await PlaywrightSetUpFixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Api-Key"] = _runner.ApiKey }
        });

        await Context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });

        Page = await Context.NewPageAsync();
        await Page.GotoAsync(RootUri);

        // Wait for app shell ready — `app-shell` testid is annotated in frontend/src/App/PageContent.tsx
        // by Plan-04 wrapper sweep. If the testid is not yet present (Wave 1 before wrappers landed),
        // fall back to title check. Use a shorter timeout for the testid probe so the harness boots
        // quickly when the testid hasn't been wired yet — the fallback assertion is the real gate.
        try
        {
            await Page.GetByTestId("app-shell").WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: assert page title contains "Mangarr" until app-shell testid is wired.
            // Both Microsoft.Playwright.TimeoutException (PlaywrightException subclass) AND
            // System.TimeoutException (raised by some transport-layer timeouts) are caught here.
            await Assertions.Expect(Page).ToHaveTitleAsync(new Regex("Mangarr"));
        }
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        // BL-02 (18-REVIEW): when OneTimeSetUp fails before `Page = await Context.NewPageAsync()`
        // (e.g. SeedBaselineAsync rejected by a 4xx, or Browser.NewContextAsync threw), individual
        // [Test] methods still receive a TearDown attempt. Without this null-guard, ScreenshotAsync
        // NREs and masks the original setup failure in CI artifacts.
        if (Page == null)
        {
            return;
        }

        if (TestContext.CurrentContext.Result.FailCount > 0)
        {
            var screenshotPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "screenshots",
                $"{TestContext.CurrentContext.Test.FullName}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
            await Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
        }
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync()
    {
        try
        {
            // BL-01 (18-REVIEW): guard Context inside the try so a failed OneTimeSetUp
            // (before Context = NewContextAsync) doesn't NRE on `Context.Tracing.StopAsync`
            // and mask the underlying setup exception. The finally block still runs
            // _runner?.KillAll() either way, so process cleanup is unaffected.
            if (Context != null)
            {
                var tracePath = Path.Combine(
                    TestContext.CurrentContext.TestDirectory,
                    "traces",
                    $"{GetType().Name}.zip");
                Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
                await Context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });
            }
        }
        finally
        {
            if (Context != null)
            {
                await Context.DisposeAsync();
            }

            _runner?.KillAll();
        }
    }
}
