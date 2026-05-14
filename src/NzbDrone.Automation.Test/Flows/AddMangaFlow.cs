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
        await addPage.SearchInput.FillAsync(mangaDexId);

        var resultRow = addPage.ResultRowByKey(mangaDexId);
        await resultRow.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        // Click the per-row Add button (the Link underlay carries the testid; clicking
        // it triggers handlePress → setIsNewAddMangaModalOpen(true) per
        // AddNewMangaSearchResult.tsx).
        await resultRow.GetByTestId("add-manga-add-button").ClickAsync();

        // Confirm the modal (accepts defaults: rootFolderPath/monitor/translationProfileId
        // are pre-populated from store + Settings). Navigation to /manga/{slug} happens
        // because useAddManga.onSuccess in useAddManga.ts pushes history after the POST.
        var modal = new AddMangaModal(page);
        return await modal.ConfirmAddAsync();
    }
}
