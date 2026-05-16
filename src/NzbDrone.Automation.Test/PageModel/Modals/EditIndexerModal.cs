using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// EditIndexer modal (frontend/src/Settings/Indexers/Indexers/EditIndexerModalContent.tsx).
/// Owned by Phase 20 Plan 20-04. Opens either from the AddIndexer picker (after
/// selecting a schema card) or from clicking an existing indexer card.
///
/// Locator strategy (debug-30 2026-05-16): The shared FormLabel
/// (frontend/src/Components/Form/FormLabel.tsx) renders `&lt;label htmlFor={name}&gt;`
/// but the consuming `&lt;FormLabel&gt;{translate('Name')}&lt;/FormLabel&gt;` doesn't
/// pass the `name` prop. With no for-attribute and no parent-of-input
/// relationship between label and input, Playwright's GetByLabel cannot
/// resolve the FormInputGroup's underlying input. We target the input
/// by its `name=` attribute instead (which TextInput propagates verbatim
/// from FormInputGroup's `name` prop — see TextInput.tsx:180).
/// </summary>
public class EditIndexerModal : PageBase
{
    public EditIndexerModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot     => Page.GetByTestId("edit-indexer-modal");
    public ILocator NameInput     => ModalRoot.Locator("input[name='name']");
    public ILocator PriorityInput => ModalRoot.Locator("input[name='priority']");
    public ILocator SaveButton    => ModalRoot.GetByTestId("save-button");
    public ILocator TestButton    => ModalRoot.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true });
    public ILocator DeleteButton  => ModalRoot.GetByTestId("delete-button");
}
