using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// EditNotification modal (frontend/src/Settings/Notifications/Notifications/EditNotificationModalContent.tsx).
/// Owned by Phase 20 Plan 20-06. Opens either from the AddNotification picker
/// (after selecting a schema card) or from clicking an existing notification card.
///
/// Locator strategy (debug-30 2026-05-16): GetByLabel-by-text fails because
/// (a) the shared FormLabel does not propagate `htmlFor` to its associated input
/// (FormLabel.tsx:35), and (b) the notification trigger CheckInputs ("On Rename" /
/// "On Manga Rename" — names from NotificationEventItems.tsx) match `GetByLabel("Name")`
/// as a substring under Playwright's default partial-text matching, producing
/// "strict mode violation: resolved to 2 elements" failures. Anchoring on the
/// underlying input's `name=` attribute resolves both issues. The Url / ApiKey /
/// LibraryId fields are ProviderFieldFormGroup-rendered with the JSON-camelCase
/// of the C# property name from {Komga,Kavita}NotificationSettings (Url → "url",
/// etc.).
/// </summary>
public class EditNotificationModal : PageBase
{
    public EditNotificationModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot      => Page.GetByTestId("edit-notification-modal");
    public ILocator NameInput      => ModalRoot.Locator("input[name='name']");
    public ILocator UrlInput       => ModalRoot.Locator("input[name='url']");
    public ILocator ApiKeyInput    => ModalRoot.Locator("input[name='apiKey']");
    public ILocator LibraryIdInput => ModalRoot.Locator("input[name='libraryId']");
    public ILocator SaveButton     => ModalRoot.GetByTestId("save-button");
    public ILocator TestButton     => ModalRoot.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true });
    public ILocator DeleteButton   => ModalRoot.GetByTestId("delete-button");
}
