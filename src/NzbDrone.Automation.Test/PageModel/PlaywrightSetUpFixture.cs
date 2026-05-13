using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

// IMPORTANT: SetUpFixture must live at the assembly root namespace (`NzbDrone.Automation.Test`)
// rather than a narrower sub-namespace. NUnit's [SetUpFixture] runs its [OneTimeSetUp] for every
// fixture whose namespace starts with the SetUpFixture's namespace. Placing this class in
// `NzbDrone.Automation.Test.PageModel` would mean fixtures under
// `NzbDrone.Automation.Test.Tests.Routes` would see `Browser == null` and fail with
// NullReferenceException in their OneTimeSetUp. File path is kept under PageModel/ for grouping;
// the namespace is the load-bearing thing.
namespace NzbDrone.Automation.Test;

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
