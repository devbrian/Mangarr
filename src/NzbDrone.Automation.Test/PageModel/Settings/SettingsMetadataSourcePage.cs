using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — /settings/metadatasource PageObject. No page-level Save button
// (SettingsToolbar.showSave={false}); per-provider rows save inline.
public class SettingsMetadataSourcePage : PageBase
{
    public SettingsMetadataSourcePage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-metadata-source-page");

    public async Task<SettingsMetadataSourcePage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/metadatasource");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsMetadataSourcePage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/metadatasource$"));
        }

        return this;
    }
}
