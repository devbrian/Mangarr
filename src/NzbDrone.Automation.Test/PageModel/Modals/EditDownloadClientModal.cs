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

    // debug-30 (2026-05-16): GetByLabel-by-text fails because shared FormLabel
    // does not propagate `htmlFor` (FormLabel.tsx:35 takes `name` prop, but the
    // call site `<FormLabel>{translate('Name')}</FormLabel>` never passes it).
    // Anchor on the underlying input's `name=` attribute (TextInput.tsx:180
    // mirrors FormInputGroup's `name` prop onto the rendered <input>).
    public ILocator NameInput                    => ModalRoot.Locator("input[name='name']");

    // Priority lives inside a FormGroup gated `isAdvanced={true}` so it does not
    // render until `<AdvancedSettingsButton>` is toggled on. Use AdvancedToggle
    // first before locating PriorityInput.
    public ILocator PriorityInput                => ModalRoot.Locator("input[name='priority']");
    public ILocator AdvancedToggle               => ModalRoot.GetByTestId("settings-advanced-toggle");

    // debug-30 (2026-05-16): InProcessImageDownloadClientSettings.DownloadsPerSource
    // serializes to JSON as `downloadsPerSource` (Newtonsoft camelCase default), which
    // is what ProviderFieldFormGroup passes as the input `name=` attribute. Anchor on
    // that contract (resilient to label-text rewording / future i18n key adoption).
    public ILocator MaxConcurrentDownloadsInput  => ModalRoot.Locator("input[name='downloadsPerSource']");
    public ILocator SaveButton                   => ModalRoot.GetByTestId("save-button");
    public ILocator TestButton                   => ModalRoot.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true });
    public ILocator DeleteButton                 => ModalRoot.GetByTestId("delete-button");
}
