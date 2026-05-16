using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.Flows;

/// <summary>
/// Phase 20 D-08 (Wave 2 shared) — picker → fill → Save sequence for
/// ThingiProvider settings forms. Used by IndexerAddEditDeleteFixture +
/// IndexerNegativeValidationFixture (Plan 20-04) and by
/// DownloadClient/Notification CRUD fixtures (Plans 20-05 / 20-06).
///
/// Mirrors the AddMangaFlow.cs shape from Phase 18 D-08: static class,
/// IPage carrier (no instance state), each method returns when the next
/// observable state is reached.
///
/// Settings/Indexers (and the other ThingiProvider settings pages) do NOT
/// expose an "Add" button — instead there is an empty card with the ADD icon
/// that opens the picker (frontend/src/Settings/Indexers/Indexers/Indexers.tsx
/// line ~73). Plan 20-04 adds a `settings-{vertical}-add-card` testid on that
/// empty card (Phase 19 D-08 fix-inline-when-contained precedent) so the
/// flow can target it deterministically.
///
/// Sonarr divergence: no upstream peer. Phase 18 D-08 elevates cross-fixture
/// orchestration sequences to first-class helpers; this is the Settings sibling
/// of AddMangaFlow.
/// </summary>
public static class SettingsProviderFlow
{
    /// <summary>
    /// Open the picker modal on Settings/{vertical} and click the canonical
    /// implementation card. Returns when the Edit modal is visible.
    /// </summary>
    /// <param name="page">Active Playwright page.</param>
    /// <param name="vertical">e.g. "indexer", "downloadclient", "notification".</param>
    /// <param name="implementationSlug">Lowercased schema slug (e.g. "mangadex").</param>
    public static async Task OpenPickerAndSelectAsync(IPage page, string vertical, string implementationSlug)
    {
        var addCard = page.GetByTestId($"settings-{vertical}-add-card");
        await Assertions.Expect(addCard).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await addCard.ClickAsync();

        var picker = page.GetByTestId($"add-{vertical}-modal");
        await Assertions.Expect(picker).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GetByTestId($"add-{vertical}-{implementationSlug}").ClickAsync();

        var editModal = page.GetByTestId($"edit-{vertical}-modal");
        await Assertions.Expect(editModal).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Click the Save button on the open Edit modal and wait for the canonical
    /// 2xx response. Asserts the modal closes afterwards.
    /// </summary>
    public static async Task SaveAsync(IPage page, string vertical)
    {
        var editModal = page.GetByTestId($"edit-{vertical}-modal");

        // WR-04 (20-REVIEW): Contains() over /api/v5/{vertical} false-positives on
        // /api/v5/{vertical}/test, /api/v5/{vertical}/schema, /api/v5/{vertical}factory,
        // etc. The save click may trigger a chain of requests; we want only the canonical
        // create (POST /api/v5/{vertical}) or update (PUT /api/v5/{vertical}/{id}). Anchor
        // the predicate on the resource boundary so unrelated chain requests do not
        // resolve the wait early.
        var saveTask = page.WaitForResponseAsync(
            r => Regex.IsMatch(r.Url, $@"/api/v5/{vertical}(/\d+)?(\?.*)?$")
                 && (r.Request.Method == "POST" || r.Request.Method == "PUT"),
            new() { Timeout = 30_000 });

        await editModal.GetByTestId("save-button").ClickAsync();
        await saveTask;

        await Assertions.Expect(editModal).ToBeHiddenAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Delete the row with the given name from Settings/{vertical} — opens the
    /// Edit modal via the card, clicks Delete, confirms the dialog, and asserts
    /// the row disappears.
    /// </summary>
    public static async Task DeleteByNameAsync(IPage page, string vertical, string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), @"\s+", "-");
        var card = page.GetByTestId($"settings-{vertical}-card-{slug}");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await card.ClickAsync();

        var editModal = page.GetByTestId($"edit-{vertical}-modal");
        await Assertions.Expect(editModal).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await editModal.GetByTestId("delete-button").ClickAsync();

        // The confirm dialog is a top-level ConfirmModal (Components/Modal/ConfirmModal.tsx)
        // with a Delete button. The Edit modal is dismissed by the delete handler before the
        // confirm dialog opens; WR-03 (20-REVIEW): explicitly wait for that dismissal before
        // resolving the confirm Delete so .Last cannot race-match the Edit modal's own Delete
        // button while both are momentarily present in the DOM.
        await Assertions.Expect(editModal).ToBeHiddenAsync(new() { Timeout = 5_000 });

        var deleteTask = page.WaitForResponseAsync(
            r => r.Url.Contains($"/api/v5/{vertical}/") && r.Request.Method == "DELETE",
            new() { Timeout = 30_000 });

        var confirmDelete = page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true });
        await Assertions.Expect(confirmDelete).ToHaveCountAsync(1, new() { Timeout = 5_000 });
        await confirmDelete.ClickAsync();
        await deleteTask;

        await Assertions.Expect(card).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }
}
