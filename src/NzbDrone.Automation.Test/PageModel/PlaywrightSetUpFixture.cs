using System;
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

    // CI-infra F1 fix (ci-test-jobs-latent-faults): because this SetUpFixture sits at the
    // assembly-root namespace (see header), NUnit also runs this [OneTimeSetUp] for the
    // unit-tier fixtures under NzbDrone.Automation.Test.TestKit — which run in the standard
    // `unit_test` job where no Playwright driver/browser is provisioned. Hard-failing here
    // took those unit-tier tests down with a Playwright "Driver not found" OneTimeSetUp error.
    //
    // Fix: tolerate a missing driver/browser. Browser stays null and the unit-tier TestKit
    // fixtures (which never touch Browser) pass. Real automation fixtures derive from
    // AutomationTest, whose [OneTimeSetUp] asserts Browser != null with a clear message —
    // so the automation tier still fails fast (not a silent null-Browser run) when the
    // browser genuinely failed to provision.
    public static bool BrowserAvailable => Browser != null;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        try
        {
            // Auto-install browser binaries if missing — keeps CI cold-start single-step.
            // Honors PLAYWRIGHT_BROWSERS_PATH per 18-RESEARCH.md line 147.
            Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });

            Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (Exception ex)
        {
            // Driver/browser not provisioned in this job (expected in the unit_test job, which
            // only selects the unit-tier NzbDrone.Automation.Test.TestKit fixtures). Leave
            // Browser null; AutomationTest.OneTimeSetUpAsync surfaces a clear assertion for the
            // automation tier. Do NOT rethrow — rethrowing fails the unit-tier TestKit tests.
            TestContext.Progress.WriteLine(
                $"[PlaywrightSetUpFixture] Playwright browser unavailable ({ex.GetType().Name}: {ex.Message}). " +
                "Automation-tier fixtures will fail fast in AutomationTest setup; unit-tier TestKit fixtures are unaffected.");
            Playwright?.Dispose();
            Playwright = null;
            Browser = null;
        }
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
