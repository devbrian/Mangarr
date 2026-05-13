using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan-03 Task 2 — Route axis PageObject for `/add/manga` (AddNewManga).
public class AddMangaPage : PageBase
{
    public AddMangaPage(IPage page)
        : base(page)
    {
    }

    public ILocator MainContainer => Page.GetByTestId("add-manga-page");

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
