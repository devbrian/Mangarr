using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// EditIndexer modal (frontend/src/Settings/Indexers/Indexers/EditIndexerModalContent.tsx).
/// Owned by Phase 20 Plan 20-04. Opens either from the AddIndexer picker (after
/// selecting a schema card) or from clicking an existing indexer card. The Name
/// + Priority inputs are labelled via translate('Name') / translate('IndexerPriority')
/// so GetByLabel resolves to the FormInputGroup's underlying input.
/// </summary>
public class EditIndexerModal : PageBase
{
    public EditIndexerModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot     => Page.GetByTestId("edit-indexer-modal");
    public ILocator NameInput     => ModalRoot.GetByLabel("Name");
    public ILocator PriorityInput => ModalRoot.GetByLabel("Indexer Priority");
    public ILocator SaveButton    => ModalRoot.GetByTestId("save-button");
    public ILocator TestButton    => ModalRoot.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true });
    public ILocator DeleteButton  => ModalRoot.GetByTestId("delete-button");
}
