using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 20 Plan 20-02 — /settings/metadata PageObject (D-17 fluent return-this).
// Greens INVENTORY route-axis row: `route | /settings/metadata | Metadata settings page renders | 🟢`.
// testid `settings-metadata-page` added inline to frontend/src/Settings/Metadata/MetadataSettings.tsx
// in the same commit (Phase 19 D-08 fix-inline-when-contained precedent).
public class SettingsMetadataPage : PageBase
{
    public SettingsMetadataPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-metadata-page");

    public async Task<SettingsMetadataPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/metadata");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsMetadataPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/metadata$"));
        }

        return this;
    }
}
