using System;
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
    /// Click the Save button on the open Edit modal, wait for the canonical
    /// POST/PUT response, fail-fast if it is non-2xx, and assert the modal
    /// closes afterwards.
    ///
    /// gh178 sub-B (2026-05-16): fail-fast (NOT click-twice retry). For
    /// verticals whose backend <c>Test()</c> makes a live outbound network
    /// call (notification — Komga/Kavita), a fixture using a placeholder URL
    /// like <c>https://komga.example</c> will see the first save POST come
    /// back 400 with a connection-test failure
    /// (ProviderControllerBase.CreateProvider:85). For those fixtures, call
    /// <see cref="BypassConnectionTestAsync"/> BEFORE the picker step so the
    /// route handler injects <c>?skipTesting=true</c> on every save POST/PUT
    /// — the first response is then 2xx and the close useEffect fires
    /// cleanly. (A frontend isResave/click-twice approach was tried and
    /// abandoned: empirically the modal's close-on-success useEffect did
    /// not fire after the retry's 2xx, likely because usePrevious(isSaving)
    /// races React's reconciliation of the first error.)
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
        //
        // debug-30 iter-2 (2026-05-16): the "notification" picker/modal vertical
        // maps to the V5 resource "connection" (ConnectionController +
        // frontend useConnections PATH='/connection'). Translate before matching.
        var resourcePath = ResourcePathFor(vertical);
        bool MatchesSave(IResponse r) =>
            Regex.IsMatch(r.Url, $@"/api/v5/{resourcePath}(/\d+)?(\?.*)?$")
            && (r.Request.Method == "POST" || r.Request.Method == "PUT");

        var saveTask = page.WaitForResponseAsync(MatchesSave, new() { Timeout = 30_000 });
        await editModal.GetByTestId("save-button").ClickAsync();
        var saveResp = await saveTask;

        if (saveResp.Status < 200 || saveResp.Status >= 300)
        {
            throw new Exception(
                $"Save returned {saveResp.Status}; expected 2xx. URL: {saveResp.Url}. "
                + $"For verticals whose backend Test() makes a live outbound call "
                + $"(notification — Komga/Kavita), call "
                + $"`SettingsProviderFlow.BypassConnectionTestAsync(Page, \"{vertical}\")` "
                + $"before the picker step to inject ?skipTesting=true so the broker "
                + $"Test() is skipped server-side (ProviderControllerBase.CreateProvider:85).");
        }

        await Assertions.Expect(editModal).ToBeHiddenAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// gh178 sub-B: register a Playwright route handler that appends
    /// <c>?skipTesting=true</c> to every POST/PUT against
    /// <c>/api/v5/{resourcePath}</c>. Use this in fixtures where the backend
    /// <c>Test()</c> would make a live outbound network call against a
    /// placeholder URL — notification/Komga + notification/Kavita.
    /// <para>
    /// Why this approach (not click-Save-twice): the frontend
    /// useManageProviderSettings.saveProvider sets <c>skipTesting=true</c> only
    /// on the "isResave" path, which requires two identical clicks with the
    /// previous error rendered in between. Empirically the second click races
    /// the React reconciliation of the first error (usePrevious(isSaving)
    /// doesn't capture the true→false flip cleanly) and the modal's
    /// close-on-success useEffect never fires on the success of the retry.
    /// Injecting the query param at the network boundary collapses the test
    /// to a single click → single 2xx → clean useEffect close — same
    /// behavior the user would get if the schema validator alone passed.
    /// </para>
    /// <para>
    /// Pair with <see cref="SaveAsync"/>: call this BEFORE the picker step
    /// (so the route is registered before any save). Auto-clears on test
    /// teardown via Playwright's per-test page lifecycle (no manual cleanup
    /// needed — AutomationTest.cs builds a fresh page per fixture).
    /// </para>
    /// </summary>
    /// <param name="page">Active Playwright page.</param>
    /// <param name="vertical">e.g. "notification" (maps to "connection").</param>
    public static async Task BypassConnectionTestAsync(IPage page, string vertical)
    {
        var resourcePath = ResourcePathFor(vertical);
        var pattern = new Regex($@"/api/v5/{resourcePath}(/\d+)?(\?.*)?$");

        await page.RouteAsync(
            url => pattern.IsMatch(url),
            async route =>
            {
                var req = route.Request;
                if (req.Method != "POST" && req.Method != "PUT")
                {
                    await route.ContinueAsync();
                    return;
                }

                // Upsert (not append): if the frontend ever sets
                // ?skipTesting=false on the outgoing URL (no current path does
                // — but the isResave branch in useProviderSettings could in
                // principle), naively appending &skipTesting=true would leave
                // duplicates and ASP.NET model binding takes the FIRST value,
                // which would still be `false`. Strip any existing
                // `skipTesting=...` entry first, then add `skipTesting=true`.
                var url = req.Url;
                var stripped = Regex.Replace(url, @"([?&])skipTesting=[^&]*&?", "$1");

                // Regex.Replace can leave trailing `?` or `&`; tidy them.
                stripped = stripped.TrimEnd('?', '&');

                var newUrl = stripped.Contains('?')
                    ? stripped + "&skipTesting=true"
                    : stripped + "?skipTesting=true";

                if (newUrl == url)
                {
                    await route.ContinueAsync();
                    return;
                }

                await route.ContinueAsync(new() { Url = newUrl });
            });
    }

    /// <summary>
    /// Translate a Settings picker/modal vertical slug to its V5 resource path.
    /// Most match 1:1 ("indexer" → "indexer", "downloadclient" → "downloadclient"),
    /// but "notification" maps to "connection" per ConnectionController +
    /// useConnections PATH='/connection'.
    /// </summary>
    public static string ResourcePathFor(string vertical) =>
        vertical == "notification" ? "connection" : vertical;

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

        // debug-30 iter-2 (2026-05-16): notification → connection (see SaveAsync note).
        var resourcePath = ResourcePathFor(vertical);
        var deleteTask = page.WaitForResponseAsync(
            r => r.Url.Contains($"/api/v5/{resourcePath}/") && r.Request.Method == "DELETE",
            new() { Timeout = 30_000 });

        var confirmDelete = page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true });
        await Assertions.Expect(confirmDelete).ToHaveCountAsync(1, new() { Timeout = 5_000 });
        await confirmDelete.ClickAsync();
        await deleteTask;

        await Assertions.Expect(card).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }
}
