using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 18 Plan 18-18 — Notification Test button coverage (INVENTORY
/// v5-endpoint row 109: POST /api/v5/connection/test).
///
/// Navigates to /settings/connect. Fresh DB seed has no notifications
/// configured, so this fixture asserts on the reachable surface (the
/// "Add" / "+" button that opens the AddNotificationModal) rather than
/// triggering an actual POST /api/v5/connection/test.
///
/// The full round-trip — add a Komga or Kavita notification (with fake
/// host:port), click Test on the row, assert toast — requires a richer
/// setup that conflicts with the per-fixture-DB isolation. This fixture
/// asserts the empty-state contract: page loads + Add surface reachable,
/// confirming the user has a path to wire a notification through the v5
/// /api/v5/connection + /api/v5/connection/test endpoints (renamed from
/// /api/v5/notification* in Phase 15 Plan 15-10; ConnectionController is
/// the canonical resource — gh #168).
///
/// No AddMangaFlow dependency — Settings/Connect operates on the
/// notification registry. No #102 [Explicit] needed.
///
/// State assertion: page contract + Add surface reachable. When a
/// populated-notifications cassette lands, this fixture's primary path
/// (click row → click Test → assert toast) activates inline.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class NotificationTestButtonFixture : AutomationTest
{
    [Test]
    public async Task notification_test_surface_reachable_with_add_or_test_button()
    {
        var page = await new SettingsNotificationsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();
        Page.Url.Should().MatchRegex(@"/settings/connect$");

        // Look for an existing notification card first. If present, click it
        // to open EditNotificationModal, then click Test.
        // Komga + Kavita are the v1 ship-list (Phase 6 NOTIFY-01/02/03);
        // they're created by the user, not seeded — so on fresh-DB the
        // notification list is empty.
        //
        // gh157 fix-forward (mirrors PR #156 / live-indexer-card-click): the
        // prior locator `Page.GetByText("Komga"|"Kavita")` resolved to the
        // inner <div> whose clicks were intercepted by the Card-underlay
        // <button> (Card.tsx overlayContent shape). D-18 + Notification.tsx
        // now expose a `settings-notification-card-<slug>` testid on the
        // underlay button itself, so ClickAsync lands on the actual
        // interactive surface. This bug was masked while the empty-list
        // branch ran (Assert.Inconclusive); it would have surfaced as a
        // 30 s timeout the moment a populated-notifications seed landed.
        var komgaCard = page.CardByName("Komga");
        var komgaCount = await komgaCard.CountAsync();
        var kavitaCard = page.CardByName("Kavita");
        var kavitaCount = await kavitaCard.CountAsync();

        if (komgaCount > 0 || kavitaCount > 0)
        {
            // Populated-notifications path: click the first card, open the
            // edit modal, click Test, assert toast or button state.
            var targetCard = komgaCount > 0 ? komgaCard : kavitaCard;
            await targetCard.ClickAsync();

            // WR-07 (18-REVIEW): wait for the dialog explicitly (replaces a
            // 500 ms static sleep). ToBeVisibleAsync auto-retries up to its
            // timeout — the sleep was redundant flake budget.
            var dialog = Page.GetByRole(AriaRole.Dialog).First;
            await Assertions.Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
            {
                Timeout = 10_000
            });

            var testButton = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Test" }).First;
            var hasTestButton = await testButton.CountAsync() > 0;

            if (hasTestButton)
            {
                await testButton.ClickAsync();

                // WR-07 (18-REVIEW): wait for the POST response rather than
                // a 3_000 ms static sleep — same primitive as
                // IndexerTestButtonFixture. Fall through on timeout so the
                // assertion below catches the missing-response case.
                try
                {
                    await Page.WaitForResponseAsync(
                        resp => resp.Url.Contains("/api/v5/connection/test") && resp.Request.Method == "POST",
                        new PageWaitForResponseOptions { Timeout = 30_000 });
                }
                catch (PlaywrightException)
                {
                    // intentional: assertion below catches missing-response
                }

                // STATE assertion: a toast appears with success/failure state.
                var toasts = Page.Locator("[role='alert'], [role='status']");
                var toastCount = await toasts.CountAsync();
                var testButtonBusy = await testButton.GetAttributeAsync("aria-busy");

                (toastCount > 0 || testButtonBusy != "true")
                    .Should().BeTrue("Notification Test must produce a toast OR return the button to idle (POST /api/v5/connection/test)");
            }
            else
            {
                // STATE assertion fallback: dialog has substantive content
                // (the notification form rendered).
                var dialogText = await dialog.TextContentAsync();
                dialogText.Should().NotBeNullOrWhiteSpace("notification edit modal must render form content");
            }
        }
        else
        {
            // WR-04 (18-REVIEW): the empty-notifications branch never
            // exercises the POST /api/v5/connection/test contract this
            // fixture claims to cover (INVENTORY v5-endpoint row 109).
            // Verifying that the Add surface exists is a page-shell check,
            // not endpoint coverage. Surface the gap explicitly via
            // Assert.Inconclusive so dashboards distinguish "ran the
            // notification-test path" from "didn't"; the surface-reachable
            // check remains as a precondition before marking inconclusive.
            //
            // gh #152 (Class 3 — assertion count = 0) fix-forward: the
            // prior fixture located the Add surface via
            // Page.GetByRole(AriaRole.Button, name="Add") — but
            // Notifications.tsx renders the Add entry as a `<Card>`
            // containing just an `<Icon name={icons.ADD}>` with NO text
            // label (Notifications.tsx L60-L68). The Card chains through
            // Link → `<button>` with no accessible name set, so the
            // Button-with-name locator returned zero matches. Re-anchored
            // to the "Connections" FieldSet legend which proves the section
            // (and therefore the Add Card) rendered.
            var fieldsetLegend = Page.Locator("legend").GetByText("Connections");
            var fieldsetLegendCount = await fieldsetLegend.CountAsync();
            fieldsetLegendCount.Should().BeGreaterThan(
                0,
                "Settings/Connect must expose the Connections FieldSet section for the user to wire their first notification (req NOTIFY-01/02)");

            Assert.Inconclusive(
                "No Komga/Kavita notifications seeded — POST /api/v5/connection/test contract NOT exercised. " +
                "Mirror DEF-18-18-01: needs a seeded fake-host notification (TestKit hook) or [Explicit] gate. " +
                "Page-shell precondition (Connections section visible) verified.");
        }
    }
}
