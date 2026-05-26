using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY modal-action row
/// `DeleteMangaFilesSelectModal` (MangaIndex bulk-select Delete-Files).
///
/// Flow: 2-manga seed via AddMangaFlow → select-mode → Select All → bulk
/// Delete Files → DeleteSelectedMangaFiles modal opens → confirm. The
/// Delete-Files path enqueues a CommandNames.DeleteSeriesFiles command
/// (per MangaIndexSelectFooter.tsx isDeleteFilesCommandExecuting hook),
/// NOT a direct REST DELETE — so the state assertion is "POST /api/v5/command
/// fires" plus modal-closes contract. Card count stays 2 (deletes files, not
/// records).
///
/// Blocker #4: 2 manga seeded upfront; zero Inconclusive.
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MangaIndexBulkDeleteFilesFixture : AutomationTest
{
    private const string KnownMangaDexId  = AddMangaFlow.KnownMangaDexId;
    private const string KnownMangaDexId2 = AddMangaFlow.KnownMangaDexId2;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task bulk_delete_files()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId2);

        var index = await new MangaIndexPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(index.PageRoot).ToBeVisibleAsync();
        await Assertions.Expect(index.Grid).ToBeVisibleAsync();

        var allCardsBefore = Page.Locator("[data-testid^='manga-card-']");
        (await allCardsBefore.CountAsync()).Should().Be(2, "two manga seeded via AddMangaFlow");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Select Manga" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Select All" }).First.ClickAsync();

        // The footer carries a "Delete Files" SpinnerButton (translate('DeleteFiles')).
        var deleteFilesButton = Page.GetByRole(AriaRole.Button,
            new PageGetByRoleOptions { Name = "Delete Files", Exact = true }).First;
        await deleteFilesButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        // STATE assertion: the bulk Delete-Files flow enqueues a DeleteSeriesFiles
        // command (POST /api/v5/command). Race the response so we don't drift past
        // the RTT before asserting card-count preservation.
        var cmdTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/command") && r.Request.Method == "POST",
            new PageWaitForResponseOptions { Timeout = 30_000 });

        await deleteFilesButton.ClickAsync();

        var modal = Page.GetByRole(AriaRole.Dialog, new() { Name = "Delete Selected Manga Files" });
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var confirmButton = modal.GetByRole(AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Delete", Exact = false });
        await confirmButton.First.ClickAsync();

        var resp = await cmdTask;
        resp.Status.Should().BeInRange(
            200,
            299,
            "DeleteSeriesFiles command must enqueue cleanly");

        // STATE assertion: card-count preserved (delete-files removes files only,
        // not the manga records themselves).
        var allCardsAfter = Page.Locator("[data-testid^='manga-card-']");
        (await allCardsAfter.CountAsync()).Should().Be(2,
            "bulk delete-files removes chapter files only — manga records remain on the index");
    }
}
