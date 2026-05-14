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
            // Browser binaries are provisioned by the CI workflow's "Provision Playwright
            // browsers" step (.github/actions/test/action.yml), which runs the shipped
            // playwright.ps1 driver script as its OWN process before the test run.
            //
            // CI-infra fix (automation-test-pr-smoke / issue #131): this method used to call
            // `Microsoft.Playwright.Program.Main(["install","chromium"])` to self-install at
            // test time. That is an anti-pattern — Program.Main is the playwright CLI's
            // process entry point, not a library API; invoking it inside a live test host
            // left the .NET<->driver transport half-initialized, so the [OneTimeTearDown]'s
            // Playwright.Dispose() P/Invoked an unbound entry point and threw
            // System.EntryPointNotFoundException. With provisioning moved to the workflow,
            // SetUpAsync only needs to CreateAsync() + LaunchAsync() against an
            // already-provisioned browser. Locally, `pwsh playwright.ps1 install chromium`
            // (or `dotnet test` after a build) provisions the same way.
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
        try
        {
            if (Browser != null)
            {
                await Browser.DisposeAsync();
            }

            Playwright?.Dispose();
        }
        catch (EntryPointNotFoundException)
        {
            // CI-infra workaround (issue #133): on .NET 10 the Microsoft.Bcl.AsyncInterfaces
            // polyfill DLL deployed into the shared _tests/ output dir shadows the in-box
            // IAsyncDisposable, so Playwright's IBrowser.DisposeAsync() throws a bare
            // System.EntryPointNotFoundException. The browser + driver processes are reaped by
            // the CI runner's orphan-process cleanup, so this dispose is cosmetic — swallow it
            // so a teardown interop quirk cannot fail an otherwise-green suite. Removing this
            // guard is the acceptance criterion of #133.
        }
    }
}
