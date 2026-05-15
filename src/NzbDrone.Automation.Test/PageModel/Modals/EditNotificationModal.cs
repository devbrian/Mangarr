using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// EditNotification modal (frontend/src/Settings/Notifications/Notifications/EditNotificationModalContent.tsx).
/// Owned by Phase 20 Plan 20-06. Opens either from the AddNotification picker
/// (after selecting a schema card) or from clicking an existing notification card.
///
/// Field labels are sourced from each provider's NotificationSettings [FieldDefinition]
/// attributes (rendered by ProviderFieldFormGroup):
///   - Komga: "Komga URL" / "API Key" / "Library ID"
///   - Kavita: "Kavita URL" / "API Key" / "Library ID (optional)"
///
/// UrlInput and LibraryIdInput expose generic accessors via `GetByLabel(regex)` so
/// the same PageObject works for both Komga and Kavita CRUD fixtures without
/// per-provider subclassing. ApiKeyInput uses the exact label "API Key" (shared
/// across both schemas verbatim).
/// </summary>
public class EditNotificationModal : PageBase
{
    public EditNotificationModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot      => Page.GetByTestId("edit-notification-modal");
    public ILocator NameInput      => ModalRoot.GetByLabel("Name");

    // Provider URL field: Komga renders "Komga URL", Kavita renders "Kavita URL".
    // Regex matches either to keep the PageObject provider-agnostic.
    public ILocator UrlInput       => ModalRoot.GetByLabel(new System.Text.RegularExpressions.Regex(@"(Komga|Kavita) URL"));

    public ILocator ApiKeyInput    => ModalRoot.GetByLabel("API Key");

    // Komga: "Library ID"; Kavita: "Library ID (optional)". Regex catches both.
    public ILocator LibraryIdInput => ModalRoot.GetByLabel(new System.Text.RegularExpressions.Regex(@"Library ID"));

    public ILocator SaveButton     => ModalRoot.GetByTestId("save-button");
    public ILocator TestButton     => ModalRoot.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true });
    public ILocator DeleteButton   => ModalRoot.GetByTestId("delete-button");
}
