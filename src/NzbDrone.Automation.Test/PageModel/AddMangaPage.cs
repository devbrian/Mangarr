using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

/// <summary>
/// AddManga page object. Phase 18 Plan-04 ships the minimal shape needed
/// by AddMangaSearchFixture (search input + per-row result locator). Phase 18
/// Plan-03 owns the canonical full version with route-axis
/// AddMangaPageLoadFixture; union-merges on Mangarr-v0 merge.
/// </summary>
public class AddMangaPage : PageBase
{
    public AddMangaPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageRoot          => Page.GetByTestId("add-manga-page");
    public ILocator SearchInput       => Page.GetByTestId("add-manga-search-input");
    public ILocator SearchClearButton => Page.GetByTestId("add-manga-search-clear-button");

    /// <summary>Locate a single search-result row by its external id (mangaDexId / aniListId / malId).</summary>
    public ILocator ResultRowByKey(string key) => Page.GetByTestId($"add-manga-result-{key}");

    public async Task<AddMangaPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/add/manga");
        return await WaitForLoadedAsync();
    }

    public async Task<AddMangaPage> WaitForLoadedAsync()
    {
        try
        {
            await PageRoot.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/add/manga$"));
        }

        return this;
    }
}
