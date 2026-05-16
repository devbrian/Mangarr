using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Activity;

/// <summary>
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — RemoveQueueItem modal
/// coverage (INVENTORY modal-action row 163: RemoveQueueItemModal).
///
/// Tier (D-04): Nightly per the axis-based heuristic — `modal-action` row.
///
/// Distinct from QueueRowRemoveFixture (the canonical per-row Remove fixture
/// covering DELETE /api/v5/manga/queue/{id}). This fixture is the
/// modal-action row: it focuses on the modal-open / confirm-close cycle of
/// the RemoveQueueItem confirmation dialog itself, asserting the modal
/// renders + confirm fires.
///
/// State assertion: seeded row > 0 → click Remove icon-button → modal
/// renders → confirm → seeded row detaches.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class RemoveQueueItemModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
    }

    [Test]
    public async Task remove_confirm()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, mangaTitle) = await ResolveSeededMangaAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedPendingQueueItemAsync(Runner.AppData, mangaId, mangaTitle);

        await new MangaQueuePage(Page).OpenAsync(RootUri);

        // STATE assertion 1: populated table mounts.
        await Assertions.Expect(Page.GetByTestId("manga-queue-table")).ToBeVisibleAsync();
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$"));
        var countBefore = await rowsLocator.CountAsync();
        countBefore.Should().BeGreaterThan(
            0,
            "D-03 seed must have produced a MangaPendingReleases queue row");

        var firstRow = rowsLocator.First;
        var firstRowIdAttr = await firstRow.GetAttributeAsync("data-testid");
        var firstRowId = firstRowIdAttr!.Replace("manga-queue-row-", string.Empty);

        // QueueRow.tsx exposes a SpinnerIconButton with title='RemoveFromQueue'
        // ("Remove from queue") — Playwright matches by accessible name substring.
        var removeButton = firstRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove" }).First;
        await removeButton.ClickAsync();

        // STATE assertion 2: the RemoveQueueItem modal renders (role=dialog).
        // Wait for VISIBLE not just attached so the Modal-modalBackdrop transition
        // has settled — clicking mid-animation lets the backdrop intercept the
        // pointer event (failure mode documented in MangaIndexBulkActionsFixture).
        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // STATE assertion 3: the modal exposes the canonical "Remove" confirm
        // button (RemoveQueueItemModal renders a "Remove" SpinnerErrorButton).
        var confirmButton = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove" }).Last;
        await Assertions.Expect(confirmButton).ToBeVisibleAsync();
        await confirmButton.ClickAsync();

        // STATE assertion 4: the doomed row detaches — the modal's confirm
        // path actually fired DELETE /api/v5/manga/queue/{id} and the row was
        // removed (not just the modal closing on its own).
        var doomedRow = Page.GetByTestId($"manga-queue-row-{firstRowId}");
        await doomedRow.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 30_000
        });
        var doomedCount = await doomedRow.CountAsync();
        doomedCount.Should().Be(0, "RemoveQueueItem modal confirm must detach the row");
    }

    private async Task<(int MangaId, string MangaTitle)> ResolveSeededMangaAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var mangaJson = await http.GetStringAsync($"{RootUri}/api/v5/manga");
        using var mangaDoc = JsonDocument.Parse(mangaJson);
        var manga = mangaDoc.RootElement[0];
        var mangaId = manga.GetProperty("id").GetInt32();
        var mangaTitle = manga.GetProperty("title").GetString()!;

        return (mangaId, mangaTitle);
    }
}
