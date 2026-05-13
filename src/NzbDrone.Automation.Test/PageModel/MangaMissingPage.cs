using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan-03 Task 2 — Route axis PageObject for `/manga/wanted/missing` (MangaMissing).
public class MangaMissingPage : PageBase
{
    public MangaMissingPage(IPage page)
        : base(page)
    {
    }

    public ILocator MainContainer => Page.GetByTestId("manga-missing-page");

    public async Task<MangaMissingPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/manga/wanted/missing");
        return await WaitForLoadedAsync();
    }

    public async Task<MangaMissingPage> WaitForLoadedAsync()
    {
        try
        {
            await MainContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/manga/wanted/missing$"));
        }

        return this; // D-17 fluent return-this
    }
}
