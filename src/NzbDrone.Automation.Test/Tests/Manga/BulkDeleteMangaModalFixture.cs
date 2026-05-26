using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY v5-endpoint row
/// `DELETE /api/v5/manga/editor` (Bulk-delete Manga modal).
///
/// Pre-v1 Mangarr does NOT carry a standalone /manga/editor route. The
/// bulk-delete surface is reached via the MangaIndex select-mode footer
/// (translate('Delete') DANGER SpinnerButton → DeleteMangaModalContent →
/// useBulkDeleteManga which fires DELETE /api/v5/manga/editor). This fixture
/// asserts the v5-endpoint contract (status code + endpoint path), distinct
/// from MangaIndexBulkDeleteFixture which tests the modal-action surface
/// (count-transition).
///
/// Flow: 2-manga seed → select-mode → Select All → Delete → modal confirm →
/// assert DELETE /api/v5/manga/editor returns 2xx.
///
/// Blocker #4: 2 manga seeded upfront; zero inconclusive-skip branches.
/// Pitfall 10: Comix disabled.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class BulkDeleteMangaModalFixture : AutomationTest
{
    private const string KnownMangaDexId  = AddMangaFlow.KnownMangaDexId;
    private const string KnownMangaDexId2 = AddMangaFlow.KnownMangaDexId2;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task bulk_delete()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId2);

        var index = await new MangaIndexPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(index.PageRoot).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Select Manga" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Select All" }).First.ClickAsync();

        var deleteButton = Page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).First;
        await deleteButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await deleteButton.ClickAsync();

        var modal = Page.GetByRole(AriaRole.Dialog, new() { Name = "Delete Selected Manga" });
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var deleteTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/editor") && r.Request.Method == "DELETE",
            new PageWaitForResponseOptions { Timeout = 30_000 });

        var confirmButton = modal.GetByRole(AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Delete", Exact = true });
        await confirmButton.ClickAsync();

        // STATE assertion: v5-endpoint contract — DELETE /api/v5/manga/editor returns 2xx.
        var resp = await deleteTask;
        resp.Status.Should().BeInRange(
            200,
            299,
            "DELETE /api/v5/manga/editor must return 2xx (v5-endpoint contract)");
        resp.Url.Should().Contain("/api/v5/manga/editor",
            "request URL must hit the manga/editor route precisely");
    }
}
