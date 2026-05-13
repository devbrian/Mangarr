using System.Threading.Tasks;
using Microsoft.Playwright;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Flows;

/// <summary>
/// Phase 18 Plan 18-07 — Settings flow helpers per D-08.
///
/// Two pre-cataloged D-08 entries (CONTEXT.md):
///   1. <see cref="SetTranslationProfileOrderAsync"/>
///   2. <see cref="AddRootFolderAsync"/>
///
/// Both methods open the relevant settings sub-route via the PageObject and run the
/// minimum action sequence to reach the desired state. Concrete reorder / modal-path
/// interactions are scoped to the test that uses them — for Wave 2 Plan-07 we ship
/// the signatures + opener + TODO hooks; downstream verifier/learning plans wire the
/// remaining steps once the FileBrowserModal + TranslationProfile reorder UI have
/// data-testid coverage (those wrappers are owned by Plan-04 and a follow-up
/// catalog plan, respectively).
/// </summary>
public static class SettingsFlow
{
    /// <summary>
    /// D-08 catalog item — reorder a translation profile to the target ordinal.
    /// </summary>
    /// <remarks>
    /// The TranslationProfile editor uses a grid of Card components (rank is not
    /// directly visible; ordering is managed inside the EditTranslationProfileModal
    /// rather than on the list view). Once the reorder UI testid catalog ships, this
    /// method will: open the row, click the rank-up / rank-down arrow N times, then
    /// click Save on the modal. For Plan-07 the method opens the page and waits for
    /// the row to render — proving the locator path is wired.
    /// </remarks>
    public static async Task SetTranslationProfileOrderAsync(
        IPage page,
        string rootUri,
        int profileId,
        int targetOrdinal)
    {
        var pageObject = new SettingsTranslationProfilesPage(page);
        await pageObject.OpenAsync(rootUri);

        var row = pageObject.Row(profileId);
        await row.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // TODO Plan-07 follow-up: wire targetOrdinal to actual reorder UI. Frontend
        // reorder shape depends on per-profile rank arrows or drag-handles that have
        // not been catalogued for testid annotation yet (Profile cards expose
        // settings-translation-profiles-row-{id} + -name only). Until then the helper
        // proves the locator path is reachable.
        _ = targetOrdinal;
    }

    /// <summary>
    /// D-08 catalog item — add a root folder via the FileBrowser modal flow.
    /// </summary>
    /// <remarks>
    /// Opens /settings/mediamanagement and clicks the AddRootFolder button. The
    /// generic FileBrowserModal that opens does not have data-testid coverage yet
    /// (it is wide-radius infrastructure used by every path-picker field — testid
    /// annotation is out of scope for Plan-07's settings cluster). Until the modal
    /// is annotated, the helper terminates at the button click — sufficient for any
    /// test that wants to assert the modal opens.
    /// </remarks>
    public static async Task AddRootFolderAsync(
        IPage page,
        string rootUri,
        string folderPath)
    {
        var pageObject = new SettingsRootFoldersPage(page);
        await pageObject.OpenAsync(rootUri);
        await pageObject.AddButton.ClickAsync();

        // TODO Plan-07 follow-up: wire folderPath into the FileBrowserModal once it
        // grows root-folder-modal-* testids (out-of-scope for Plan-07 — the modal is
        // generic infrastructure shared with EditManga / InteractiveImport).
        _ = folderPath;
    }
}
