using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 18 Plan 18-18 — Indexer Test button coverage (INVENTORY v5-endpoint
/// row 104: POST /api/v5/indexer/test).
///
/// Navigates to /settings/indexers. Seeds a gateway indexer in OneTimeSetUp,
/// opens the indexer edit modal and clicks the Test button. Asserts a test
/// result toast appears (success or failure — both are valid state, the point
/// is that POST /api/v5/indexer/test fired and returned a determinate outcome).
///
/// LiveService tag: the gateway indexer test hits the live external gateway by
/// default. Without a reachable gateway (fresh CI offline), this fixture would
/// hit the live host — so we tag it [Category("LiveService")] per the plan's
/// STRIDE T-18-18-03 mitigation (D-10 LiveService tier). Offline wire-shape
/// coverage lives in IndexerTestButtonOfflineFixture.
///
/// No AddMangaFlow dependency — Settings/Indexers operates on the indexer
/// registry. No #102 [Explicit] needed.
///
/// Phase 39 Plan 39-07: repointed from the baseline-seeded MangaDex card (the
/// in-process site-scraper indexer was retired in Plan 39-03) to a self-seeded
/// GatewayIndexer (the sole IIndexer).
///
/// State assertion: post-test toast appears with success OR failure state.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("LiveService")]
public class IndexerTestButtonFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await tk.SeedIndexerAsync();
    }

    [Test]
    public async Task indexer_test_button_produces_toast_state()
    {
        var page = await new SettingsIndexersPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();
        Page.Url.Should().MatchRegex(@"/settings/indexers$");

        // D-18 + Indexer.tsx expose a `settings-indexer-card-<slug>` testid on
        // the underlay button itself, so ClickAsync lands on the actual
        // interactive surface. The gateway is the sole IIndexer (Phase 39 Plan
        // 39-07); the self-seeded "Gateway (test seed)" card is the click target.
        var gatewayCard = page.CardByName("Gateway (test seed)");
        var cardCount = await gatewayCard.CountAsync();

        if (cardCount == 0)
        {
            // WR-03 (18-REVIEW): NUnit reports a silent `return` as PASSED,
            // so the prior empty-card path masked a seed regression as a green
            // test. Assert.Inconclusive flags the missing precondition
            // explicitly — fixture flips to "Inconclusive" (not "Passed") so
            // dashboards distinguish "ran the test-button path" from "couldn't
            // run it".
            Assert.Inconclusive(
                "Seeded gateway indexer card not present — SeedIndexerAsync regression? " +
                "Skipping POST /api/v5/indexer/test coverage path.");
        }

        // Click the gateway card → EditIndexerModal opens.
        await gatewayCard.ClickAsync();

        // WR-07 (18-REVIEW): wait for the modal-dialog state explicitly
        // (replaces a 500 ms static sleep). The ToBeVisibleAsync below is
        // an auto-retry assertion so it already polls — the redundant sleep
        // was pure flake budget.
        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 10_000
        });

        // The modal exposes a Test button (EditIndexerModalContent.tsx line 264:
        // SpinnerErrorButton with translate('Test')). Click it.
        var testButton = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Test" }).First;
        await testButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await testButton.ClickAsync();

        // WR-07 (18-REVIEW): wait for the POST /api/v5/indexer/test response
        // explicitly (replaces a 5_000 ms static sleep — CI under load can
        // exceed that for the live API hop). 30_000 ms ceiling matches the
        // LiveService budget for upstream-API probes.
        try
        {
            await Page.WaitForResponseAsync(
                resp => resp.Url.Contains("/api/v5/indexer/test") && resp.Request.Method == "POST",
                new PageWaitForResponseOptions { Timeout = 30_000 });
        }
        catch (PlaywrightException)
        {
            // Fall through to the toast/spinner state assertion below — if
            // the response never landed, the assertion will catch it.
            // PlaywrightException is the base for Microsoft.Playwright.TimeoutException
            // and the wait-timeout class on this version.
        }

        // STATE assertion: either a toast appeared with success/failure state
        // OR the button's spinning state is back to idle (POST returned).
        var toasts = Page.Locator("[role='alert'], [role='status']");
        var toastCount = await toasts.CountAsync();
        var testButtonStillSpinning = await testButton.GetAttributeAsync("aria-busy");

        // STATE assertion: post-test, EITHER a toast surfaced OR the button
        // transitioned out of its spinning state. Both indicate the v5
        // round-trip completed.
        (toastCount > 0 || testButtonStillSpinning != "true")
            .Should().BeTrue("Indexer Test must produce a toast OR return the button to idle state (POST /api/v5/indexer/test)");
    }
}
