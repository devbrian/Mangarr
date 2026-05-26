using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY modal-action row
/// `MoveMangaModal` (EditManga RootFolder change → MoveManga confirm).
///
/// Phase 17.3 GH #81 enhancement (CLOSED 2026-05-12): MoveMangaModal exists +
/// MoveMangaCommand wired. EditMangaModalContent.tsx routes through this confirm
/// dialog when the user changes the manga's path on Save — MoveFiles header
/// per MoveMangaModal.tsx.
///
/// Flow: seed a second root folder via TestKit.SeedRootFolderAsync → AddMangaFlow
/// seed → Edit modal → typed path change directly into the Path input (bypasses
/// the RootFolder picker hop for determinism — the MoveMangaModal triggers when
/// pendingChanges.path differs from manga.path, regardless of how the change
/// was applied) → click Save → MoveMangaModal opens → click "No, I'll Move the
/// Files Manually" → PUT /api/v5/manga/{id} fires with moveFiles=false.
///
/// State assertion: MoveFiles dialog renders AND the PUT /api/v5/manga/{id}
/// returns 2xx after the "Don't Move Files" path is taken (non-destructive).
///
/// Blocker #4: 1 manga + 1 second root folder seeded upfront; zero
/// inconclusive-skip branches.
/// Pitfall 10: Comix disabled.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MoveMangaModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    private string _secondRootFolder = string.Empty;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        _secondRootFolder = Path.Combine(Runner.AppData, "MangaLibrary2");
        Directory.CreateDirectory(_secondRootFolder);
        await tk.SeedRootFolderAsync(_secondRootFolder);
    }

    [Test]
    public async Task move_confirm()
    {
        var details = await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        await details.EditButton.ClickAsync();
        var editModal = new EditMangaModal(Page);
        await Assertions.Expect(editModal.ModalRoot).ToBeVisibleAsync();

        // The Path FormInputGroup carries an input bound to `path`. Locate it
        // within the edit-manga-modal and overwrite with the second-root path
        // so pendingChanges.path differs from manga.path on Save — that's what
        // triggers MoveMangaModal per EditMangaModalContent.tsx
        // (handleSavePress's path-changed gate).
        var pathInput = editModal.ModalRoot.Locator("input[name='path']");
        await pathInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        var newPath = Path.Combine(_secondRootFolder, "Komi");
        await pathInput.FillAsync(newPath);

        // Click Save on the EditManga modal. Because path differs, MoveMangaModal
        // opens (the confirm-move gate) instead of the modal closing immediately.
        await editModal.SaveButton.ClickAsync();

        // STATE assertion: the MoveFiles dialog rendered.
        var moveModal = Page.GetByRole(AriaRole.Dialog, new() { Name = "Move Files" });
        await Assertions.Expect(moveModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Pick the non-destructive "No, I'll Move the Files Manually" path so the
        // backend doesn't enqueue the BulkMoveMangaCommand on a fresh empty disk.
        var putTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/") && r.Request.Method == "PUT",
            new PageWaitForResponseOptions { Timeout = 30_000 });

        var noMoveButton = moveModal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions
        {
            Name = "No, I'll Move the Files Manually",
            Exact = false
        });
        await noMoveButton.First.ClickAsync();

        var resp = await putTask;
        resp.Status.Should().BeInRange(
            200,
            299,
            "PUT /api/v5/manga/{id} with moveFiles=false must round-trip cleanly");
    }
}
