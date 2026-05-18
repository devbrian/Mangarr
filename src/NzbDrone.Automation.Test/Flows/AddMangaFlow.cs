using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NzbDrone.Automation.Test.PageModel;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Flows;

/// <summary>
/// Phase 18 D-08 first-class shared UI flow helper. Any cluster test (Plan-05..Plan-09)
/// that needs a manga in the library uses this helper instead of re-walking the
/// AddManga UI from scratch.
///
/// Per Phase 18 D-06 (UI-populates-via-UI principle): library state should be seeded
/// by driving the actual UI, not by direct API/DB writes. AddMangaFlow is the canonical
/// D-06 implementation — every cluster fixture that needs manga state seeds via this
/// flow rather than POSTing /api/v5/manga directly. The cassette-replayed MangaDex
/// backend (TestKit/CassetteHandler + Fixtures/Cassettes/MangaDex/*.json) makes this
/// deterministic + network-OFF.
///
/// Sonarr divergence: no upstream peer. Sonarr's Selenium tests called PageObject
/// methods directly without naming the cross-page sequence as a first-class helper.
/// D-08 elevates the sequence to a named helper.
///
/// Role-match analog: src/NzbDrone.Core/IndexerSearch/Manga/MangaSearchService.cs
/// — an orchestrator that composes lower-level operations (IMangaSearchForReleases +
/// IProcessMangaDownloadDecisions) into one named higher-level task. Same shape:
/// static class + static methods, IPage is the carrier (no instance state).
/// </summary>
public static class AddMangaFlow
{
    /// <summary>
    /// Canonical first test-anchor MangaDex UUID — Komi Can't Communicate
    /// (Komi-san wa Komyushou Desu.). Used as the default seed in single-manga fixtures.
    /// </summary>
    public const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    /// <summary>
    /// Canonical second test-anchor MangaDex UUID — Chainsaw Man. Used by
    /// MangaIndexBulkActionsFixture and any future fixture that requires 2
    /// distinct manga in the seed state (avoids unique-key collision on the
    /// Manga.MangaDexId column).
    /// </summary>
    public const string KnownMangaDexId2 = "a77742b1-befd-49a4-bff5-1ad4e6b0ef7b";

    /// <summary>
    /// Add a manga by MangaDex ID, accepting the modal defaults (root folder, monitor,
    /// translation profile from Settings). Returns the resulting MangaDetailsPage where
    /// the user lands after the post-add navigation.
    /// </summary>
    /// <param name="page">Active Playwright page for the test.</param>
    /// <param name="rootUri">Mangarr backend root URI (honor port mobility — never hard-code :8989).</param>
    /// <param name="mangaDexId">Stable MangaDex UUID used as the row identifier in the search-result row testid.</param>
    public static async Task<MangaDetailsPage> AddByMangaDexIdAsync(IPage page, string rootUri, string mangaDexId)
    {
        var addPage = await new AddMangaPage(page).OpenAsync(rootUri);

        // Fill the search input + submit. The Mangarr AddManga page debounces (500ms per
        // AddNewManga.tsx) — pressing Enter does not bypass debounce, so simply fill + wait
        // for the result row. The result row is keyed by mangaDexId per
        // AddNewMangaSearchResult.tsx's data-testid={`add-manga-result-${mangaDexId}`}.
        //
        // 2026-05-18: live MangaDex `/manga/{id}` calls can transiently fail or
        // rate-limit when several fixtures run in close succession (observed on
        // Phase 24 smoke gate Run #1 + #3 + #4: bulk_edit + bulk_save + bulk_delete
        // + bulk_tags + bulk_organize intermittently failed on the second test manga
        // `a77742b1-...` (Chainsaw Man) — different subset each run, all failing on
        // the result-row wait. Single retry inside a 60s window wasn't enough; some
        // rate-limit windows last 90s+. We retry up to 4 total attempts with
        // progressive sleep (1s, 10s, 30s) between retriggers to ride out longer
        // throttle windows without masking a real backend regression (a code-bug
        // failure would still fail every attempt, taking ~120s+ total before
        // surfacing).
        await addPage.SearchInput.FillAsync(mangaDexId);

        var resultRow = addPage.ResultRowByKey(mangaDexId);
        var retryWaitsMs = new[] { 1_000, 10_000, 30_000 };
        var attempt = 0;
        while (true)
        {
            try
            {
                await resultRow.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
                break;
            }
            catch (PlaywrightException) when (attempt < retryWaitsMs.Length)
            {
                await addPage.SearchInput.FillAsync(string.Empty);
                await page.WaitForTimeoutAsync(retryWaitsMs[attempt]);
                await addPage.SearchInput.FillAsync(mangaDexId);
                attempt++;
            }
        }

        // Click the per-row Add button (the Link underlay carries the testid; clicking
        // it triggers handlePress → setIsNewAddMangaModalOpen(true) per
        // AddNewMangaSearchResult.tsx).
        await resultRow.GetByTestId("add-manga-add-button").ClickAsync();

        // Confirm the modal. Per Sonarr-mirror UX (useAddManga.onSuccess only updates
        // the React Query cache — no history.push, matching Sonarr's useAddSeries shape
        // verified pre-Phase-15-delete), the modal auto-closes on add success but the
        // user stays on /add/manga. Issue #102 close-out 2026-05-14.
        var modal = new AddMangaModal(page);

        // Debug session pr-smoke-add-manga-timeout (2026-05-14): the modal's
        // RootFolderSelectInput replaces the zustand store's default empty
        // rootFolderPath with the first real root folder only once the async
        // useRootFolders() query resolves. On a loaded CI runner, clicking Add
        // before that lands POSTs /api/v5/manga with rootFolderPath: '' and the
        // backend PathValidator 400s — the modal then correctly stays open and
        // ConfirmAddAsync's hidden-wait times out. AddNewMangaModalContent now
        // disables the Add button while rootFolderPath is empty, so wait for the
        // button to be enabled (root folder populated) before confirming. This
        // removes the timing race from the test side regardless of query latency.
        await modal.WaitForReadyToAddAsync();
        await modal.ConfirmAddAsync();

        // Navigate explicitly to /manga/{slug} via the library index, since the frontend
        // doesn't auto-navigate. The freshly-added card is now in the cache (onSuccess
        // wrote it to the ['/manga'] queryKey synchronously). Click-through takes us to
        // MangaDetailsPage. This adapter preserves the existing fixture contract
        // (callers still get a MangaDetailsPage) without forcing a frontend divergence
        // from Sonarr.
        await new MangaIndexPage(page).OpenAsync(rootUri);

        // First card on the index — testid is `manga-card-{titleSlug}` per
        // MangaIndexPoster.tsx:137. We don't know the slug ahead of time
        // (it's derived backend-side from the manga title) so match the prefix.
        var card = page.Locator("[data-testid^='manga-card-']").First;
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await Task.WhenAll(
            page.WaitForURLAsync(new Regex(@"/manga/[^/]+$")),
            card.ClickAsync());
        return new MangaDetailsPage(page);
    }
}
