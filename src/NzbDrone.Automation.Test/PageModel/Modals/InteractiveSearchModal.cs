using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

// Phase 18 Plan-08 (D-08 pre-cataloged) -- InteractiveSearchModal PageObject.
//
// The plan-prescribed name presumed a dedicated InteractiveSearchModal.tsx
// component (Sonarr shape). In Mangarr the canonical render is
// frontend/src/InteractiveSearch/InteractiveSearch.tsx, surfaced EITHER
// as a tab pane in MangaDetails (activeTab === 'search') OR inside the
// ChapterDetailsModal wrapper. Plan-08 Task 1 annotates the content surface
// with `interactive-search-modal` so GetByTestId resolves in both contexts.
//
// OpenForMangaAsync uses the MangaDetails tab path because that surface is
// the canonical D-06 "UI-populates-via-UI" entry point for per-manga release
// browsing (no separate modal button needed -- the tab IS the entry).
public class InteractiveSearchModal : PageBase
{
    public InteractiveSearchModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot => Page.GetByTestId("interactive-search-modal");
    public ILocator Table => Page.GetByTestId("interactive-search-modal-table");
    public ILocator NoResults => Page.GetByTestId("interactive-search-modal-no-results");

    /// <summary>
    /// Navigate to a manga details page (by titleSlug) and switch to the Search tab.
    /// Returns only once the interactive search has reached a settled steady state —
    /// EITHER the results table is rendered OR the explicit no-results indicator is
    /// shown. Both are valid terminal states; the in-between `isFetching` state
    /// (LoadingIndicator) is NOT a steady state and must not be observed by callers.
    /// </summary>
    public async Task<InteractiveSearchModal> OpenForMangaAsync(string rootUri, string mangaSlug)
    {
        await Page.GotoAsync($"{rootUri}/manga/{mangaSlug}");

        // Click the Search tab. MangaDetails.tsx renders tabs as <button role="tab">
        // wired to `setActiveTab('search')`; Playwright's text-based locator is
        // resilient against tab restyling and ordering changes.
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Search" })
                  .ClickAsync();

        // The `interactive-search-modal` wrapper div mounts as soon as the Search tab
        // is active — it is present DURING the in-flight `isFetching` state, before any
        // release fetch completes. Waiting only for it (the prior behaviour) returned
        // while the LoadingIndicator was still showing, so callers raced the async
        // /api/v5/manga/release fetch: GetReleaseCountAsync saw 0 rows and the fixture
        // mis-classified a healthy results-bearing search as "no results".
        //
        // Wait for ModalRoot first (cheap, always-true gate) then for the SETTLED
        // steady state: the results table OR the no-results indicator. InteractiveSearch.tsx
        // renders exactly one of those two once `!isFetching && isFetched`.
        await ModalRoot.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await WaitForSettledAsync();
        return this;
    }

    /// <summary>
    /// Block until the interactive search has settled — the results table OR the
    /// no-results indicator is visible. Mirrors InteractiveSearch.tsx's two terminal
    /// render branches (table when results came back, no-results indicator when the
    /// settled search returned nothing). Throws on timeout if neither appears.
    /// </summary>
    public async Task WaitForSettledAsync(int timeoutMs = 30_000)
    {
        await Table.Or(NoResults).First
                   .WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
    }

    /// <summary>
    /// Count the visible release rows. Matches testids of shape
    /// `interactive-search-row-{guid}` (UUIDs contain hyphens, so the regex
    /// uses a negative lookahead that excludes only the known suffixed cell
    /// testids: -title, -decision, -rejected-icon, -grab-button).
    /// </summary>
    public async Task<int> GetReleaseCountAsync()
    {
        // BL-03 fix (18-13): the prior `[^-]+$` regex rejected UUID-shaped GUIDs
        // because UUIDs contain hyphens. Match any suffix except the four known
        // cell-testid tokens emitted by InteractiveSearchRow.tsx.
        var rows = Page.GetByTestId(new Regex(@"^interactive-search-row-(?!.*-(title|decision|rejected-icon|grab-button)$).+$"));
        return await rows.CountAsync();
    }

    /// <summary>
    /// Click the Grab button on the Nth release row (0-indexed). Resolves the
    /// row's testid attribute to derive the guid, then clicks the grab-button
    /// suffixed testid.
    ///
    /// Waits for the grab POST (/api/v5/manga/release) to RECEIVE A RESPONSE before
    /// returning. The grab handler (InteractiveSearchRow.tsx -> useGrabMangaRelease)
    /// is a fire-and-forget React Query mutation — the click returns immediately while
    /// the POST is still in flight. A caller that navigates away (e.g. GotoAsync to
    /// the History page) right after the click cancels the in-flight POST before the
    /// backend commits the grab (observed as a 499 client-closed-request), so no queue
    /// row and no history row ever materialise. Awaiting the response makes the grab
    /// observably durable before the caller moves on.
    /// </summary>
    public async Task GrabAsync(int releaseIndex)
    {
        // BL-03 fix (18-13): UUID-aware row regex (see GetReleaseCountAsync).
        var rows = Page.GetByTestId(new Regex(@"^interactive-search-row-(?!.*-(title|decision|rejected-icon|grab-button)$).+$"));
        var row = rows.Nth(releaseIndex);
        var idAttr = await row.GetAttributeAsync("data-testid");
        if (string.IsNullOrEmpty(idAttr))
        {
            throw new System.InvalidOperationException(
                "Row at index " + releaseIndex + " has no data-testid attribute");
        }

        var guid = idAttr!.Replace("interactive-search-row-", string.Empty);

        // Arm the response wait BEFORE the click so the in-flight POST cannot be
        // missed, then click and await the POST completing. The manga grab path
        // POSTs to /api/v5/manga/release (useReleases.ts getGrabPath — chapter/manga
        // payloads route to the manga controller).
        await Page.RunAndWaitForResponseAsync(
            async () =>
            {
                await Page.GetByTestId("interactive-search-row-" + guid + "-grab-button").ClickAsync();
            },
            response => response.Url.Contains("/api/v5/manga/release")
                        && response.Request.Method == "POST");
    }
}
