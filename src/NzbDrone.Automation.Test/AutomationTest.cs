using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NLog;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;
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
        _runner = new NzbDroneRunner(LogManager.GetCurrentClassLogger(), null);
        _runner.KillAll();
        _runner.Start(enableAuth: true);

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
        // fall back to title check.
        try
        {
            await Page.GetByTestId("app-shell").WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        }
        catch (PlaywrightException)
        {
            // Fallback: assert page title contains "Mangarr" until app-shell testid is wired.
            // PlaywrightException covers TimeoutException (which derives from PlaywrightException
            // in Microsoft.Playwright 1.59.0) plus any other locator-related fault.
            await Assertions.Expect(Page).ToHaveTitleAsync(new Regex("Mangarr"));
        }
    }

    [TearDown]
    public async Task TearDownAsync()
    {
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
            var tracePath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "traces",
                $"{GetType().Name}.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
            await Context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });
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
