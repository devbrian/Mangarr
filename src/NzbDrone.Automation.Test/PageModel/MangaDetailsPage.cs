using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan-03 Task 2 — Route axis PageObject for `/manga/:titleSlug` (MangaDetails).
// Phase 18 Plan-04 added Edit/Delete/Refresh/Search/History button locators (union-merged).
// MainContainer and PageRoot are aliases for the same testid — kept both so each plan's fixtures compile.
public class MangaDetailsPage : PageBase
{
    public MangaDetailsPage(IPage page)
        : base(page)
    {
    }

    public ILocator MainContainer     => Page.GetByTestId("manga-details-page");
    public ILocator PageRoot          => MainContainer;
    public ILocator EditButton        => Page.GetByTestId("manga-details-edit-button");
    public ILocator DeleteButton      => Page.GetByTestId("manga-details-delete-button");
    public ILocator RefreshButton     => Page.GetByTestId("manga-details-refresh-button");
    public ILocator ManualSearchButton => Page.GetByTestId("manga-details-manual-search-button");
    public ILocator HistoryButton     => Page.GetByTestId("manga-details-history-button");

    public async Task<MangaDetailsPage> OpenAsync(string rootUri, string titleSlug)
    {
        await Page.GotoAsync($"{rootUri}/manga/{titleSlug}");
        return await WaitForLoadedAsync();
    }

    public async Task<MangaDetailsPage> WaitForLoadedAsync()
    {
        try
        {
            await MainContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/manga/[^/]+$"));
        }

        return this; // D-17 fluent return-this
    }
}
