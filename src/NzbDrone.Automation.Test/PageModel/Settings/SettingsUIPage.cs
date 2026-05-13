using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — /settings/ui PageObject.
public class SettingsUIPage : PageBase
{
    public SettingsUIPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-ui-page");
    public ILocator SaveButton    => Page.GetByTestId("settings-save-button");

    public async Task<SettingsUIPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/ui");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsUIPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/ui$"));
        }

        return this;
    }
}
