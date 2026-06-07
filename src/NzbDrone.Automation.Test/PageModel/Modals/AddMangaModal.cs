using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// AddManga side-panel modal (AddNewMangaModalContent.tsx). Owned by Phase 18 Plan-04.
/// Used by AddMangaFlow.AddByMangaBakaIdAsync as the final-step modal where the user
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
    /// MangaDetailsPage should use AddMangaFlow.AddByMangaBakaIdAsync, which
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

        // 2026-05-18: Phase 24 smoke gate Run #4 showed `bulk_save` failing with the
        // modal stuck visible 30s after Confirm. The POST /api/v5/manga path calls
        // MangaDex MetadataSource to refresh the manga (now including artist
        // relationship per Phase 24 D-03) — on a rate-limited window the backend can
        // take longer than the modal's 30s hide-wait to respond. Retry the click up
        // to 3 times with a 15s progressive backoff (the modal stays open and clickable
        // on failed POSTs, so re-clicking re-submits). Same anti-mask-bug discipline
        // as the AddMangaFlow result-row retry — a real backend regression would
        // fail every attempt and surface up after ~120s+ total.
        var modalConfirmAttempts = 3;
        for (var attempt = 0; attempt < modalConfirmAttempts; attempt++)
        {
            try
            {
                // Race-safe: if a slow backend response arrived during the 15s
                // backoff between attempts and the modal already closed, do
                // NOT re-click — the AddButton would no longer be attached to
                // the DOM and ClickAsync would throw, masking the successful
                // add. Check before each click and bail early on success.
                if (await ModalRoot.IsHiddenAsync())
                {
                    return;
                }

                await AddButton.ClickAsync();
                await ModalRoot.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Hidden,
                    Timeout = 30_000
                });
                return;
            }
            catch (System.Exception ex) when (attempt < modalConfirmAttempts - 1 && (ex is System.TimeoutException || ex is PlaywrightException))
            {
                // Playwright .NET throws System.TimeoutException (not PlaywrightException)
                // when LocatorWaitForOptions.Timeout expires — observed on Phase 24
                // smoke gate Run #5. Match both for robust retry coverage.
                await Page.WaitForTimeoutAsync(15_000);
            }
        }
    }
}
