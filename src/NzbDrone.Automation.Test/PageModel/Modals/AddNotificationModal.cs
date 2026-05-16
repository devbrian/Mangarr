using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// AddNotification picker modal (frontend/src/Settings/Notifications/Notifications/AddNotificationModalContent.tsx).
/// Owned by Phase 20 Plan 20-06. Opened by clicking the empty add card on
/// /settings/connect; lists schema implementations from GET /api/v5/notification/schema.
///
/// Slug derivation mirrors Plans 20-04 + 20-05 (AddIndexerItem / AddDownloadClientItem
/// suffix-strip pattern): implementation.replace(/Indexer$/i, '').replace(
/// /DownloadClient$/i, '').replace(/Notification$/i, '').toLowerCase().
/// For "KomgaNotification" → "komga"; for "KavitaNotification" → "kavita".
/// D-06 canonical Notification pick is Komga; Kavita is the same-family alternative
/// shipped alongside (NOTIFY-01 + NOTIFY-02).
/// </summary>
public class AddNotificationModal : PageBase
{
    public AddNotificationModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot   => Page.GetByTestId("add-notification-modal");
    public ILocator KomgaCard   => Page.GetByTestId("add-notification-komga");
    public ILocator KavitaCard  => Page.GetByTestId("add-notification-kavita");

    public ILocator SchemaCard(string slug)
        => Page.GetByTestId($"add-notification-{slug}");
}
