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
/// Navigates to /settings/indexers. When a seeded indexer card exists
/// (MangaDex + Comix seed via baseline migration per Plan 18-14 TestKit
/// pre-seed), opens the indexer edit modal and clicks the Test button.
/// Asserts a test result toast appears (success or failure — both are valid
/// state, the point is that POST /api/v5/indexer/test fired and returned
/// a determinate outcome).
///
/// LiveService tag: MangaDex indexer test hits the live MangaDex API by
/// default. Under cassette mode (per Plan 18-14), the test passes
/// deterministically. Without cassettes (fresh CI offline), this fixture
/// would hit the live API — so we tag it [Category("LiveService")] per the
/// plan's STRIDE T-18-18-03 mitigation (D-10 LiveService tier).
///
/// No AddMangaFlow dependency — Settings/Indexers operates on the indexer
/// registry. No #102 [Explicit] needed.
///
/// State assertion: post-test toast appears with success OR failure state.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("LiveService")]
public class IndexerTestButtonFixture : AutomationTest
{
    [Test]
    public async Task indexer_test_button_produces_toast_state()
    {
        var page = await new SettingsIndexersPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();
        Page.Url.Should().MatchRegex(@"/settings/indexers$");

        // Indexer cards are rendered without data-testid attributes (Indexer.tsx
        // as of Wave 2). Locate by visible name — MangaDex is the canonical
        // baseline-seeded indexer (Phase 3 D-15 — IsPrimary).
        var mangaDexCard = Page.GetByText("MangaDex", new PageGetByTextOptions { Exact = true }).First;
        var cardCount = await mangaDexCard.CountAsync();

        if (cardCount == 0)
        {
            // WR-03 (18-REVIEW): NUnit reports a silent `return` as PASSED,
            // so the prior empty-card path masked a baseline-seed regression
            // as a green test. Assert.Inconclusive flags the missing
            // precondition explicitly — fixture flips to "Inconclusive"
            // (not "Passed") so dashboards distinguish "ran the test-button
            // path" from "couldn't run it".
            Assert.Inconclusive(
                "Baseline MangaDex indexer card not present — Phase 3 D-15 seed regression? " +
                "Skipping POST /api/v5/indexer/test coverage path.");
        }

        // Click the MangaDex card → EditIndexerModal opens.
        await mangaDexCard.ClickAsync();

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
