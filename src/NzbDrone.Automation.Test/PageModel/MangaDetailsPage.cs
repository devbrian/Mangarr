using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

/// <summary>
/// MangaDetails page object. Phase 18 Plan-04 ships the minimal shape needed
/// by AddMangaFlow (return type) + Edit/Delete modal fixtures (button locators).
/// Phase 18 Plan-03 owns the canonical full version with route-axis fixture
/// support — when both plans merge into Mangarr-v0, the union of locators
/// applies. Locators added here match the data-testid-spec.md `manga-details-*`
/// naming so Plan-03's richer version can extend without breaking selectors.
/// </summary>
public class MangaDetailsPage : PageBase
{
    public MangaDetailsPage(IPage page)
        : base(page)
    {
    }

    // Page-scoped locators (data-testid-spec.md §"`{page}-{element}`")
    public ILocator PageRoot           => Page.GetByTestId("manga-details-page");
    public ILocator EditButton         => Page.GetByTestId("manga-details-edit-button");
    public ILocator DeleteButton       => Page.GetByTestId("manga-details-delete-button");
    public ILocator RefreshButton      => Page.GetByTestId("manga-details-refresh-button");
    public ILocator ManualSearchButton => Page.GetByTestId("manga-details-manual-search-button");
    public ILocator HistoryButton      => Page.GetByTestId("manga-details-history-button");

    public async Task<MangaDetailsPage> WaitForLoadedAsync()
    {
        // Best-effort: wait for the page testid (short timeout so we fail-fast to URL fallback
        // when the testid hasn't been wired yet). When Plan-04 wrappers land it's present.
        try
        {
            await PageRoot.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: assert URL matches /manga/{slug} pattern
            await Page.WaitForURLAsync(new Regex(@"/manga/[^/]+$"));
        }

        return this; // D-17 fluent return-this
    }
}
