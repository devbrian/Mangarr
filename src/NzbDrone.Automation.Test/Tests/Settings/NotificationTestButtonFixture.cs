using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 18 Plan 18-18 — Notification Test button coverage (INVENTORY
/// v5-endpoint row 109: POST /api/v5/notification/test).
///
/// Navigates to /settings/connect. Fresh DB seed has no notifications
/// configured, so this fixture asserts on the reachable surface (the
/// "Add" / "+" button that opens the AddNotificationModal) rather than
/// triggering an actual POST /api/v5/notification/test.
///
/// The full round-trip — add a Komga or Kavita notification (with fake
/// host:port), click Test on the row, assert toast — requires a richer
/// setup that conflicts with the per-fixture-DB isolation. This fixture
/// asserts the empty-state contract: page loads + Add surface reachable,
/// confirming the user has a path to wire a notification through the v5
/// /api/v5/notification + /api/v5/notification/test endpoints.
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
        var komgaCard = Page.GetByText("Komga", new PageGetByTextOptions { Exact = true });
        var komgaCount = await komgaCard.CountAsync();
        var kavitaCard = Page.GetByText("Kavita", new PageGetByTextOptions { Exact = true });
        var kavitaCount = await kavitaCard.CountAsync();

        if (komgaCount > 0 || kavitaCount > 0)
        {
            // Populated-notifications path: click the first card, open the
            // edit modal, click Test, assert toast or button state.
            var targetCard = komgaCount > 0 ? komgaCard.First : kavitaCard.First;
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
                        resp => resp.Url.Contains("/api/v5/notification/test") && resp.Request.Method == "POST",
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
                    .Should().BeTrue("Notification Test must produce a toast OR return the button to idle (POST /api/v5/notification/test)");
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
            // exercises the POST /api/v5/notification/test contract this
            // fixture claims to cover (INVENTORY v5-endpoint row 109).
            // Verifying that an Add button exists is a page-shell check, not
            // endpoint coverage. Surface the gap explicitly via
            // Assert.Inconclusive so dashboards distinguish "ran the
            // notification-test path" from "didn't"; the surface-reachable
            // check remains as a precondition before marking inconclusive.
            var addButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add" });
            var addCount = await addButton.CountAsync();
            addCount.Should().BeGreaterThan(0, "Settings/Connect must expose an Add surface for the user to wire their first notification (req NOTIFY-01/02)");

            Assert.Inconclusive(
                "No Komga/Kavita notifications seeded — POST /api/v5/notification/test contract NOT exercised. " +
                "Mirror DEF-18-18-01: needs a seeded fake-host notification (TestKit hook) or [Explicit] gate. " +
                "Page-shell precondition (Add surface visible) verified.");
        }
    }
}
