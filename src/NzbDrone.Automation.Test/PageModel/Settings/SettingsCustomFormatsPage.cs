using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — /settings/customformats PageObject. No page-level Save button
// (SettingsToolbar.showSave={false}); custom-format rows have their own edit modals.
public class SettingsCustomFormatsPage : PageBase
{
    public SettingsCustomFormatsPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-custom-formats-page");

    public async Task<SettingsCustomFormatsPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/customformats");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsCustomFormatsPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/customformats$"));
        }

        return this;
    }
}
