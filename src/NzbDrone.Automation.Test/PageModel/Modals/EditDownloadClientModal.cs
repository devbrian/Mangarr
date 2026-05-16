using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// EditDownloadClient modal (frontend/src/Settings/DownloadClients/DownloadClients/EditDownloadClientModalContent.tsx).
/// Owned by Phase 20 Plan 20-05. Opens either from the AddDownloadClient picker
/// (after selecting a schema card) or from clicking an existing download client card.
///
/// Priority is labelled via translate('ClientPriority') = "Client Priority" (en.json),
/// not the IndexerPriority key used in 20-04. The InProcessImageDownloadClient
/// concurrency field (DOWNLOAD-02) is rendered by ProviderFieldFormGroup from the
/// FieldDefinition Label="InProcessDownloadsPerSource" attribute on
/// InProcessImageDownloadClientSettings.cs (no en.json translation key exists, so
/// translate() falls back to the raw key per frontend/src/Utilities/String/translate.ts).
/// </summary>
public class EditDownloadClientModal : PageBase
{
    public EditDownloadClientModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot                    => Page.GetByTestId("edit-downloadclient-modal");
    public ILocator NameInput                    => ModalRoot.GetByLabel("Name");
    public ILocator PriorityInput                => ModalRoot.GetByLabel("Client Priority");

    // WR-02 (20-REVIEW): no en.json translation key exists yet for
    // "InProcessDownloadsPerSource", so translate() falls back to the raw key today.
    // Match either the raw key OR a likely future English translation ("Downloads
    // Per Source") so this locator survives the day someone adds an i18n entry.
    public ILocator MaxConcurrentDownloadsInput  => ModalRoot.GetByLabel(new Regex(@"^(InProcessDownloadsPerSource|Downloads Per Source)$"));
    public ILocator SaveButton                   => ModalRoot.GetByTestId("save-button");
    public ILocator TestButton                   => ModalRoot.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true });
    public ILocator DeleteButton                 => ModalRoot.GetByTestId("delete-button");
}
