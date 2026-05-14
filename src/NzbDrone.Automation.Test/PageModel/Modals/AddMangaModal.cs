using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// AddManga side-panel modal (AddNewMangaModalContent.tsx). Owned by Phase 18 Plan-04.
/// Used by AddMangaFlow.AddByMangaDexIdAsync as the final-step modal where the user
/// confirms an add; returns the resulting MangaDetailsPage after the post-add navigation.
/// </summary>
public class AddMangaModal : PageBase
{
    public AddMangaModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot    => Page.GetByTestId("add-manga-modal");
    public ILocator AddButton    => Page.GetByTestId("add-manga-modal-add-button");
    public ILocator CancelButton => Page.GetByTestId("add-manga-modal-cancel-button");

    /// <summary>
    /// Wait for the modal's Add button to become enabled.
    ///
    /// Debug session pr-smoke-add-manga-timeout (2026-05-14): AddNewMangaModalContent
    /// disables `add-manga-modal-add-button` while `rootFolderPath` is empty. The
    /// RootFolderSelectInput only replaces the zustand store's default empty
    /// rootFolderPath with the first real root folder once the async useRootFolders()
    /// query resolves — so "button enabled" is the deterministic signal that the
    /// root-folder select has a non-empty selected value. Waiting on it here removes
    /// the timing race from the test side regardless of frontend query latency.
    /// </summary>
    public async Task WaitForReadyToAddAsync()
    {
        await Assertions.Expect(AddButton).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions
        {
            Timeout = 30_000
        });
    }

    /// <summary>
    /// Click the modal's Add button and wait for the modal to close.
    ///
    /// Issue #102 close-out 2026-05-14: Sonarr-mirror UX verified against
    /// pre-Phase-15-delete useAddSeries.ts — `onSuccess` only updates the
    /// React Query cache; no history.push. The modal auto-closes on add
    /// success but the user stays on /add/manga. Callers needing the
    /// MangaDetailsPage should use AddMangaFlow.AddByMangaDexIdAsync, which
    /// adapts via an explicit MangaIndex → card click navigation step.
    ///
    /// Debug session pr-smoke-add-manga-timeout (2026-05-14): waits for the Add
    /// button to be enabled (root folder populated) before clicking — see
    /// WaitForReadyToAddAsync. Without this the click could fire while
    /// rootFolderPath is still empty, the POST 400s, and the modal correctly
    /// stays open until this wait times out.
    /// </summary>
    public async Task ConfirmAddAsync()
    {
        await WaitForReadyToAddAsync();

        await AddButton.ClickAsync();

        // Wait for the modal to disappear from the DOM (the AddManga component
        // unmounts the side-panel modal when isNewAddMangaModalOpen flips to false
        // inside useAddManga.onSuccess). 30 s ceiling matches the search-row timeout.
        await ModalRoot.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 30_000
        });
    }
}
