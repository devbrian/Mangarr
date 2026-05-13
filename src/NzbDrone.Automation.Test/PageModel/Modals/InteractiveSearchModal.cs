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
    /// Returns when either the interactive-search-modal content surface is visible
    /// OR the no-results indicator appears (both are valid steady states).
    /// </summary>
    public async Task<InteractiveSearchModal> OpenForMangaAsync(string rootUri, string mangaSlug)
    {
        await Page.GotoAsync($"{rootUri}/manga/{mangaSlug}");

        // Click the Search tab. MangaDetails.tsx renders tabs as <button role="tab">
        // wired to `setActiveTab('search')`; Playwright's text-based locator is
        // resilient against tab restyling and ordering changes.
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Search" })
                  .ClickAsync();

        // Steady state: ModalRoot rendered (results in OR empty).
        await ModalRoot.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        return this;
    }

    /// <summary>
    /// Count the visible release rows. Matches testids of shape
    /// `interactive-search-row-{guid}` (excludes the suffixed cell testids by
    /// requiring no trailing `-` segment).
    /// </summary>
    public async Task<int> GetReleaseCountAsync()
    {
        var rows = Page.GetByTestId(new Regex(@"^interactive-search-row-[^-]+$"));
        return await rows.CountAsync();
    }

    /// <summary>
    /// Click the Grab button on the Nth release row (0-indexed). Resolves the
    /// row's testid attribute to derive the guid, then clicks the grab-button
    /// suffixed testid.
    /// </summary>
    public async Task GrabAsync(int releaseIndex)
    {
        var rows = Page.GetByTestId(new Regex(@"^interactive-search-row-[^-]+$"));
        var row = rows.Nth(releaseIndex);
        var idAttr = await row.GetAttributeAsync("data-testid");
        if (string.IsNullOrEmpty(idAttr))
        {
            throw new System.InvalidOperationException(
                "Row at index " + releaseIndex + " has no data-testid attribute");
        }

        var guid = idAttr!.Replace("interactive-search-row-", string.Empty);
        await Page.GetByTestId("interactive-search-row-" + guid + "-grab-button").ClickAsync();
    }
}
