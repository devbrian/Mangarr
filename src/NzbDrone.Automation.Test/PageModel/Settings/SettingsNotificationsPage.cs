using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — /settings/connect PageObject (route slug 'connect' is Sonarr's
// historic name for the Notifications cluster; the page title is "Notifications"). No
// page-level Save button exists — SettingsToolbar.showSave={false} — per Task 1 note;
// per-provider rows save inline via their own modal flows.
public class SettingsNotificationsPage : PageBase
{
    public SettingsNotificationsPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-notifications-page");

    // gh157 fix-forward (mirrors SettingsIndexersPage.CardByName / PR #156) —
    // D-18 selector for a notification card. The testid lands on the
    // Card-underlay <button> (Card.tsx overlay branch), so ClickAsync()
    // targets the actual interactive surface rather than the inner text <div>.
    // Slug = lowercase name with whitespace collapsed to dashes (see
    // Notification.tsx).
    public ILocator CardByName(string name)
        => Page.GetByTestId($"settings-notification-card-{Slugify(name)}");

    private static string Slugify(string name)
        => Regex.Replace(name.ToLowerInvariant(), @"\s+", "-");

    public async Task<SettingsNotificationsPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/connect");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsNotificationsPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/connect$"));
        }

        return this;
    }
}
