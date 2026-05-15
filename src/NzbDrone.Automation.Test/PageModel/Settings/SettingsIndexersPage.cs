using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — /settings/indexers PageObject.
public class SettingsIndexersPage : PageBase
{
    public SettingsIndexersPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer    => Page.GetByTestId("settings-indexers-page");
    public ILocator SaveButton       => Page.GetByTestId("settings-save-button");
    public ILocator TestAllButton    => Page.GetByTestId("settings-indexers-test-all-button");
    public ILocator ManageButton     => Page.GetByTestId("settings-indexers-manage-button");

    // gh152-uat fix-forward (live-indexer-card-click) — D-18 selector for an
    // indexer card. The testid lands on the Card-underlay <button>
    // (Card.tsx overlay branch), so ClickAsync() targets the actual
    // interactive surface rather than the inner text <div>. Slug = lowercase
    // name with whitespace collapsed to dashes (see Indexer.tsx).
    public ILocator CardByName(string name)
        => Page.GetByTestId($"settings-indexer-card-{Slugify(name)}");

    private static string Slugify(string name)
        => Regex.Replace(name.ToLowerInvariant(), @"\s+", "-");

    public async Task<SettingsIndexersPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/indexers");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsIndexersPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/indexers$"));
        }

        return this; // D-17 fluent return-this
    }
}
