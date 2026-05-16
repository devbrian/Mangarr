using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// EditIndexer modal (frontend/src/Settings/Indexers/Indexers/EditIndexerModalContent.tsx).
/// Owned by Phase 20 Plan 20-04. Opens either from the AddIndexer picker (after
/// selecting a schema card) or from clicking an existing indexer card.
///
/// Locator strategy (GH #180 2026-05-16): Inputs are anchored on the D-18
/// `data-testid` contract — `settings-indexer-field-*` — emitted by the
/// FormInputGroup wrapper layer (frontend/src/Components/Form/FormInputGroup.tsx
/// forwards data-testid through {...otherProps} to TextInput, which renders
/// `data-testid` on the underlying input element per TextInput.tsx:185).
/// Replaces the prior `input[name='...']` CSS-selector fallback that worked
/// around FormLabel's missing htmlFor wiring; the testid is the contract going
/// forward.
/// </summary>
public class EditIndexerModal : PageBase
{
    public EditIndexerModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot     => Page.GetByTestId("edit-indexer-modal");
    public ILocator NameInput     => ModalRoot.GetByTestId("settings-indexer-field-name");

    // Priority lives inside a FormGroup gated `isAdvanced={true}` so it does not
    // render until `<AdvancedSettingsButton>` is toggled on. Use AdvancedToggle
    // first before locating PriorityInput.
    public ILocator PriorityInput => ModalRoot.GetByTestId("settings-indexer-field-priority");
    public ILocator AdvancedToggle => ModalRoot.GetByTestId("settings-advanced-toggle");
    public ILocator SaveButton    => ModalRoot.GetByTestId("save-button");
    public ILocator TestButton    => ModalRoot.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true });
    public ILocator DeleteButton  => ModalRoot.GetByTestId("delete-button");
}
