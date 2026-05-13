using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan-03 Task 2 — Route axis PageObject for `/` (MangaIndex).
// Phase 18 Plan-04 added grid + per-card locators for AddManga/Delete cluster fixtures (union-merged).
// MainContainer and PageRoot are aliases for the same testid — kept both so each plan's fixtures compile.
public class MangaIndexPage : PageBase
{
    public MangaIndexPage(IPage page)
        : base(page)
    {
    }

    public ILocator MainContainer => Page.GetByTestId("manga-index-page");
    public ILocator PageRoot      => MainContainer;
    public ILocator Grid          => Page.GetByTestId("manga-index-grid");

    /// <summary>Locate the per-card cell by titleSlug or mangaId.</summary>
    public ILocator CardByKey(string keyOrSlug) => Page.GetByTestId($"manga-card-{keyOrSlug}");

    public async Task<MangaIndexPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/");
        return await WaitForLoadedAsync();
    }

    public async Task<MangaIndexPage> WaitForLoadedAsync()
    {
        try
        {
            await MainContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Root URL ends with `/` (no `/manga` prefix on Mangarr v1 — `/` is the index per AppRoutes.tsx).
            await Page.WaitForURLAsync(new Regex(@"/(\?.*)?$"));
        }

        return this; // D-17 fluent return-this
    }
}
