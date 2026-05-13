using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.PageModel;

[SetUpFixture]
public class PlaywrightSetUpFixture
{
    public static IPlaywright Playwright { get; private set; }
    public static IBrowser Browser { get; private set; }

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        // Auto-install browser binaries if missing — keeps CI cold-start single-step.
        // Honors PLAYWRIGHT_BROWSERS_PATH per 18-RESEARCH.md line 147.
        Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    [OneTimeTearDown]
    public async Task TearDownAsync()
    {
        if (Browser != null)
        {
            await Browser.DisposeAsync();
        }

        Playwright?.Dispose();
    }
}
