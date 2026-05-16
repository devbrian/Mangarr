using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// EditNotification modal (frontend/src/Settings/Notifications/Notifications/EditNotificationModalContent.tsx).
/// Owned by Phase 20 Plan 20-06. Opens either from the AddNotification picker
/// (after selecting a schema card) or from clicking an existing notification card.
///
/// Locator strategy (GH #180 2026-05-16): Inputs are anchored on the D-18
/// `data-testid` contract — `settings-notification-field-*` — emitted by the
/// FormInputGroup wrapper layer for the explicitly-rendered Name field and
/// derived from `field.name` by ProviderFieldFormGroup for the dynamically-
/// rendered Url / ApiKey / LibraryId fields (Newtonsoft camelCase JSON of the
/// C# property names from {Komga,Kavita}NotificationSettings). Replaces the
/// prior `input[name='...']` CSS-selector fallback, which had two failure
/// modes documented in debug-30 (FormLabel htmlFor gap + the "On Manga Rename"
/// CheckInput label colliding with GetByLabel('Name') under partial-text
/// matching).
/// </summary>
public class EditNotificationModal : PageBase
{
    public EditNotificationModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot      => Page.GetByTestId("edit-notification-modal");
    public ILocator NameInput      => ModalRoot.GetByTestId("settings-notification-field-name");
    public ILocator UrlInput       => ModalRoot.GetByTestId("settings-notification-field-url");
    public ILocator ApiKeyInput    => ModalRoot.GetByTestId("settings-notification-field-apiKey");
    public ILocator LibraryIdInput => ModalRoot.GetByTestId("settings-notification-field-libraryId");
    public ILocator SaveButton     => ModalRoot.GetByTestId("save-button");
    public ILocator TestButton     => ModalRoot.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true });
    public ILocator DeleteButton   => ModalRoot.GetByTestId("delete-button");
}
