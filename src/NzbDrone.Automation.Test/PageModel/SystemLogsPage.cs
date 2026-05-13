using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

public class SystemLogsPage : PageBase
{
    public SystemLogsPage(IPage page)
        : base(page)
    {
    }

    public ILocator LogsPanel => Page.GetByTestId("system-logs-page");

    public async Task<SystemLogsPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/system/logs/files");
        return await WaitForLoadedAsync();
    }

    public async Task<SystemLogsPage> WaitForLoadedAsync()
    {
        try
        {
            await LogsPanel.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: assert URL ends with /system/logs/files — testid added in Plan-09 System cluster
            await Page.WaitForURLAsync(new Regex(@"/system/logs/files$"));
        }

        return this; // D-17 fluent return-this
    }
}
