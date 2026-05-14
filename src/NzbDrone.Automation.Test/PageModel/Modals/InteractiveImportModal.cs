using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

// Phase 18 Plan-08 -- InteractiveImportModal PageObject.
//
// InteractiveImport is typically triggered from a manual-import flow
// (System > Tasks > Manual Import OR a /add/import route once that lands).
// The modal mount has the `interactive-import-modal` testid wrapper applied
// in Plan-08 Task 1 (frontend/src/InteractiveImport/InteractiveImportModal.tsx);
// the table inside has `interactive-import-modal-table`.
public class InteractiveImportModal : PageBase
{
    public InteractiveImportModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot => Page.GetByTestId("interactive-import-modal");
    public ILocator Table => Page.GetByTestId("interactive-import-modal-table");
}
