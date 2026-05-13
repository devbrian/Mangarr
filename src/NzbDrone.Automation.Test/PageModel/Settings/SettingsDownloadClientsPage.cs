using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — /settings/downloadclients PageObject.
public class SettingsDownloadClientsPage : PageBase
{
    public SettingsDownloadClientsPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-download-clients-page");
    public ILocator SaveButton    => Page.GetByTestId("settings-save-button");
    public ILocator TestAllButton => Page.GetByTestId("settings-download-clients-test-all-button");
    public ILocator ManageButton  => Page.GetByTestId("settings-download-clients-manage-button");

    public async Task<SettingsDownloadClientsPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/downloadclients");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsDownloadClientsPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/downloadclients$"));
        }

        return this;
    }
}
