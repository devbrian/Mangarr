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
    /// D-08 catalog item — reorder the FIRST language inside the TranslationProfile
    /// identified by <paramref name="profileId"/> down (or up) to <paramref name="targetOrdinal"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The TranslationProfile editor renders its languages as a flat ordered list of
    /// BCP-47 string rows with up / down move buttons (see
    /// <c>frontend/src/Settings/Profiles/Translations/EditTranslationProfileModalContent.tsx</c>
    /// lines 14-15) — the array index IS the preference rank (Phase 5 D-03). Per
    /// 30-02-PLAN.md Task 1 Part B the locked semantics are: open the EditModal for
    /// <paramref name="profileId"/>, then move the language currently at index 0 to
    /// <paramref name="targetOrdinal"/> by clicking the per-row arrow buttons that the
    /// modal re-binds by index after every move. Phase 30 Plan 30-02 (II2-07) replaces
    /// the Phase-18 stub that discarded <paramref name="targetOrdinal"/>.
    /// </para>
    /// <para>
    /// Concrete sequence:
    ///   1. Open <c>/settings/profiles</c> and wait for the profile row.
    ///   2. Click the row to open <c>EditTranslationProfileModal</c>.
    ///   3. While current index &lt; target: click the down-arrow at the moving row's
    ///      CURRENT index (testid <c>settings-translation-profiles-edit-row-{currentIdx}-down</c>).
    ///      While current index &gt; target: click the up-arrow at the moving row's current
    ///      index (<c>...-edit-row-{currentIdx}-up</c>). After every click the modal
    ///      re-renders and the moved language sits at the next index, so the next arrow
    ///      testid binds at the new position.
    ///   4. Click Save (testid <c>settings-translation-profiles-edit-save</c>) and wait for
    ///      the modal to close.
    /// </para>
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

        // Open the EditTranslationProfileModal — Card.onPress on the row opens it.
        await row.ClickAsync();

        // Wait for the modal's Save button (anchors on the modal being open + interactive).
        await pageObject.EditSaveButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // Walk the moving row from index 0 to targetOrdinal, clicking the appropriate
        // arrow once per ordinal step. The modal re-binds testids by index after every
        // click, so the next iteration's locator binds at the new current index.
        var currentIdx = 0;
        while (currentIdx < targetOrdinal)
        {
            await pageObject.EditRowDownArrow(currentIdx).ClickAsync();
            currentIdx++;
        }

        while (currentIdx > targetOrdinal)
        {
            await pageObject.EditRowUpArrow(currentIdx).ClickAsync();
            currentIdx--;
        }

        // Save the modal. The modal auto-closes on save success (see
        // EditTranslationProfileModalContent.useEffect-on-saveSuccess).
        await pageObject.EditSaveButton.ClickAsync();

        // Wait for the modal to close (Save button leaves the DOM on auto-close).
        await pageObject.EditSaveButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 15_000
        });
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
