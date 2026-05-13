using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel;

// Phase 18 Plan 18-07: parent /settings route PageObject. Owns the canonical tab
// navigator (data-testid="settings-tab-{section}") that lives on Settings.tsx; each
// sub-route has its own dedicated PageObject under PageModel/Settings/.
public class SettingsPage : PageBase
{
    public SettingsPage(IPage page)
        : base(page)
    {
    }

    public ILocator IndexPage => Page.GetByTestId("settings-index-page");

    public ILocator TabRootFolders            => Page.GetByTestId("settings-tab-root-folders");
    public ILocator TabTranslationProfiles    => Page.GetByTestId("settings-tab-translation-profiles");
    public ILocator TabCustomFormatProfiles   => Page.GetByTestId("settings-tab-custom-format-profiles");
    public ILocator TabCustomFormats          => Page.GetByTestId("settings-tab-custom-formats");
    public ILocator TabIndexers               => Page.GetByTestId("settings-tab-indexers");
    public ILocator TabDownloadClients        => Page.GetByTestId("settings-tab-download-clients");
    public ILocator TabImportLists            => Page.GetByTestId("settings-tab-import-lists");
    public ILocator TabNotifications          => Page.GetByTestId("settings-tab-notifications");
    public ILocator TabMetadata               => Page.GetByTestId("settings-tab-metadata");
    public ILocator TabMetadataSource         => Page.GetByTestId("settings-tab-metadata-source");
    public ILocator TabTags                   => Page.GetByTestId("settings-tab-tags");
    public ILocator TabGeneral                => Page.GetByTestId("settings-tab-general");
    public ILocator TabUI                     => Page.GetByTestId("settings-tab-ui");

    public async Task<SettingsPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsPage> WaitForLoadedAsync()
    {
        try
        {
            await IndexPage.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings$"));
        }

        return this; // D-17 fluent return-this
    }
}
