using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

public class SystemUpdatesPage : PageBase
{
    public SystemUpdatesPage(IPage page)
        : base(page)
    {
    }

    public ILocator UpdatesPanel => Page.GetByTestId("system-updates-page");

    public async Task<SystemUpdatesPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/system/updates");
        return await WaitForLoadedAsync();
    }

    public async Task<SystemUpdatesPage> WaitForLoadedAsync()
    {
        try
        {
            await UpdatesPanel.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: assert URL ends with /system/updates — testid added in Plan-09 System cluster
            await Page.WaitForURLAsync(new Regex(@"/system/updates$"));
        }

        return this; // D-17 fluent return-this
    }
}
