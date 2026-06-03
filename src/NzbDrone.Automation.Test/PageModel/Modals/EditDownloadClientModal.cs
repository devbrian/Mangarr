using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// EditDownloadClient modal (frontend/src/Settings/DownloadClients/DownloadClients/EditDownloadClientModalContent.tsx).
/// Owned by Phase 20 Plan 20-05. Opens either from the AddDownloadClient picker
/// (after selecting a schema card) or from clicking an existing download client card.
///
/// Priority is labelled via translate('ClientPriority') = "Client Priority" (en.json),
/// not the IndexerPriority key used in 20-04. Phase 39 Plan 39-07: the sole download
/// client is GatewayDownloadClient (the in-process image downloader + its
/// DownloadsPerSource concurrency field were retired in Plan 39-02), so the gateway
/// client renders Host/Port/UseSsl/UrlBase/ApiKey provider fields instead.
///
/// Locator strategy (GH #180 2026-05-16): Inputs are anchored on the D-18
/// `data-testid` contract — `settings-downloadclient-field-*` — emitted by
/// the FormInputGroup wrapper layer for the explicitly-rendered fields (name,
/// priority) and derived from `field.name` by ProviderFieldFormGroup for the
/// dynamically-rendered gateway fields (host/port/apiKey). Replaces the prior
/// `input[name='...']` CSS-selector fallback.
/// </summary>
public class EditDownloadClientModal : PageBase
{
    public EditDownloadClientModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot                    => Page.GetByTestId("edit-downloadclient-modal");

    public ILocator NameInput                    => ModalRoot.GetByTestId("settings-downloadclient-field-name");

    // Priority lives inside a FormGroup gated `isAdvanced={true}` so it does not
    // render until `<AdvancedSettingsButton>` is toggled on. Use AdvancedToggle
    // first before locating PriorityInput.
    public ILocator PriorityInput                => ModalRoot.GetByTestId("settings-downloadclient-field-priority");
    public ILocator AdvancedToggle               => ModalRoot.GetByTestId("settings-advanced-toggle");

    // GatewayDownloadClientSettings.ApiKey provider field (serialized as `apiKey`).
    // ProviderFieldFormGroup derives the testid as
    // `settings-{provider}-field-{field.name}` per GH #180 scope C.
    public ILocator ApiKeyInput                  => ModalRoot.GetByTestId("settings-downloadclient-field-apiKey");
    public ILocator SaveButton                   => ModalRoot.GetByTestId("save-button");
    public ILocator TestButton                   => ModalRoot.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true });
    public ILocator DeleteButton                 => ModalRoot.GetByTestId("delete-button");
}
