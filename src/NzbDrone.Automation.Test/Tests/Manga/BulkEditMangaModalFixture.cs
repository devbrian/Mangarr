using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY v5-endpoint row
/// `PUT /api/v5/manga/editor` (Bulk-edit Manga modal Save).
///
/// Pre-v1 Mangarr does NOT carry a standalone /manga/editor route (the upstream
/// Sonarr SeriesEditor page-route was deleted in Phase 15 Plan 15-12 alongside
/// the Series/ tree). The bulk-edit surface is reached via the MangaIndex
/// select-mode footer (translate('Edit') SpinnerButton → EditMangaModalContent).
/// This fixture asserts the PUT /api/v5/manga/editor v5-endpoint contract
/// (status code + endpoint path), distinct from the MangaIndexBulkEditFixture
/// which tests the modal-action interaction surface.
///
/// Flow: 2-manga seed → select-mode → Select All → Edit → modal Save → assert
/// PUT /api/v5/manga/editor returns 2xx.
///
/// Blocker #4: 2 manga seeded upfront; zero inconclusive-skip branches.
/// Pitfall 10: Comix disabled.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class BulkEditMangaModalFixture : AutomationTest
{
    private const string KnownMangaDexId  = AddMangaFlow.KnownMangaDexId;
    private const string KnownMangaDexId2 = AddMangaFlow.KnownMangaDexId2;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task bulk_save()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId2);

        var index = await new MangaIndexPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(index.PageRoot).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Select Manga" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Select All" }).First.ClickAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).First.ClickAsync();

        var modal = Page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Selected Manga" });
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // debug-30 (2026-05-16): EditMangaModalContent.save() short-circuits
        // when no field has changed (no `hasChanges` path → no onSavePress →
        // no PUT). Flip the Monitored dropdown to "Monitored" before saving
        // so the PUT actually fires.
        await FlipMonitoredAsync(modal);

        var putTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/editor") && r.Request.Method == "PUT",
            new PageWaitForResponseOptions { Timeout = 30_000 });

        var applyButton = modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions
        {
            Name = "Apply Changes",
            Exact = false
        });
        if (await applyButton.CountAsync() == 0)
        {
            applyButton = modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions
            {
                Name = "Save",
                Exact = false
            });
        }

        await applyButton.First.ClickAsync();

        // STATE assertion: v5-endpoint contract — PUT /api/v5/manga/editor returns 2xx.
        var resp = await putTask;
        resp.Status.Should().BeInRange(
            200,
            299,
            "PUT /api/v5/manga/editor must return 2xx (v5-endpoint contract)");
        resp.Url.Should().Contain("/api/v5/manga/editor",
            "request URL must hit the manga/editor route precisely");
    }

    // EnhancedSelectInput with name="monitored" — open dropdown, pick "Monitored".
    // debug-30 iter-2 (2026-05-16): EnhancedSelectInput renders its options
    // inside <FloatingPortal id="portal-root"> (EnhancedSelectInput.tsx:457),
    // which is a SIBLING of the modal dialog — NOT a child. Page-scoped
    // GetByText("Monitored").First collides with the in-modal <FormLabel>
    // "Monitored" (EditMangaModalContent.tsx:206), which is a non-interactive
    // <label> — clicking it doesn't fire the EnhancedSelectInputOption
    // handler. Scope to #portal-root so we only see actual option entries.
    private static async Task FlipMonitoredAsync(ILocator modal)
    {
        var monitoredTrigger = modal.GetByText("No Change").First;
        await monitoredTrigger.ClickAsync();
        var monitoredOption = modal.Page.Locator("#portal-root")
            .GetByText("Monitored", new() { Exact = true })
            .First;
        await Assertions.Expect(monitoredOption).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await monitoredOption.ClickAsync();
    }
}
