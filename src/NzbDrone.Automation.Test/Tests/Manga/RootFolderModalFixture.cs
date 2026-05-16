using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY modal-action row
/// `RootFolderModal` (EditManga RootFolder picker).
///
/// Flow: AddMangaFlow seed → MangaDetails Edit button → EditMangaModal opens →
/// click the FormInputButton (RootFolder picker) on the Path FormInputGroup →
/// RootFolderModal opens (UpdateMangaPath header) → assert GET
/// /api/v5/manga/{id}/folder fires.
///
/// State assertion: GET /api/v5/manga/{id}/folder returns 200 AND modal
/// renders with the UpdateMangaPath header.
///
/// Blocker #4: 1 manga seeded upfront; zero inconclusive-skip branches.
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class RootFolderModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task picker_returns_path()
    {
        var details = await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Open the EditManga modal.
        await details.EditButton.ClickAsync();
        var editModal = new EditMangaModal(Page);
        await Assertions.Expect(editModal.ModalRoot).ToBeVisibleAsync();

        // The Path FormInputGroup carries a "Root Folder" FormInputButton trailing
        // button (title=translate('RootFolder')). Locate by title attribute, scoped
        // to the edit-manga-modal so we don't match other root-folder buttons.
        var rootFolderButton = editModal.ModalRoot.Locator(
            "[title='Root Folder'], button[title='Root Folder']").First;

        // Race the GET /api/v5/manga/{id}/folder response — the RootFolderModal
        // mounts on click and fires the useApiQuery<MangaFolder> hook.
        var folderTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/") &&
                 r.Url.Contains("/folder") &&
                 r.Request.Method == "GET",
            new PageWaitForResponseOptions { Timeout = 30_000 });

        await rootFolderButton.ClickAsync();

        var resp = await folderTask;
        resp.Status.Should().Be(200, "GET /api/v5/manga/{id}/folder must return 200 when the picker mounts");

        // STATE assertion: the RootFolder modal rendered (UpdateMangaPath header).
        var rootFolderModal = Page.GetByRole(AriaRole.Dialog, new() { Name = "Update Manga Path" });
        await Assertions.Expect(rootFolderModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
