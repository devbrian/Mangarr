using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

public class SystemBackupsPage : PageBase
{
    public SystemBackupsPage(IPage page)
        : base(page)
    {
    }

    public ILocator BackupsPanel => Page.GetByTestId("system-backups-page");

    public async Task<SystemBackupsPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/system/backup");
        return await WaitForLoadedAsync();
    }

    public async Task<SystemBackupsPage> WaitForLoadedAsync()
    {
        try
        {
            await BackupsPanel.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: assert URL ends with /system/backup — testid added in Plan-09 System cluster
            await Page.WaitForURLAsync(new Regex(@"/system/backup$"));
        }

        return this; // D-17 fluent return-this
    }
}
