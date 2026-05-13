using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan-03 Task 2 — Route axis PageObject for `/manga/wanted/cutoffunmet` (MangaCutoffUnmet).
public class MangaCutoffUnmetPage : PageBase
{
    public MangaCutoffUnmetPage(IPage page)
        : base(page)
    {
    }

    public ILocator MainContainer => Page.GetByTestId("manga-cutoff-unmet-page");

    public async Task<MangaCutoffUnmetPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/manga/wanted/cutoffunmet");
        return await WaitForLoadedAsync();
    }

    public async Task<MangaCutoffUnmetPage> WaitForLoadedAsync()
    {
        try
        {
            await MainContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/manga/wanted/cutoffunmet$"));
        }

        return this; // D-17 fluent return-this
    }
}
