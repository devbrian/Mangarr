using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan-03 Task 2 — Route axis PageObject for `/add/manga` (AddNewManga).
// Phase 18 Plan-04 added search-result locators for AddMangaSearchFixture (union-merged).
// MainContainer and PageRoot are aliases for the same testid — kept both so each plan's fixtures compile.
public class AddMangaPage : PageBase
{
    public AddMangaPage(IPage page)
        : base(page)
    {
    }

    public ILocator MainContainer     => Page.GetByTestId("add-manga-page");
    public ILocator PageRoot          => MainContainer;
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
            await MainContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/add/manga$"));
        }

        return this; // D-17 fluent return-this
    }
}
