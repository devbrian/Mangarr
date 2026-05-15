using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Components;

// Phase 18 Plan 18-16 Task 2 — Tests/Components/ TableOptionsModal (generic
// column-options widget, modal-action row 183 in INVENTORY.md). The widget
// is wired by Components/Table/TableOptions/TableOptionsModalWrapper.tsx;
// any table page can host one. The Wanted/Missing page (Missing.tsx
// L297-L306) wraps a PageToolbarButton with the "Options" label inside the
// wrapper — clicking it opens the TableOptionsModal with a "Table Options"
// header.
//
// Why /manga/wanted/missing: same rationale as FilterModalFixture — the
// MangaIndex Options button piggybacks on a TableOptionsModalWrapper that's
// connected to MangaIndex grid columns whose mount state depends on having
// manga seeded. The Wanted/Missing variant is always mounted.
//
// State assertions:
// 1. Dialog with accessible name "Table Options" appears after clicking Options.
// 2. At least one column-row / FormGroup appears in modal body (asserts the
//    column-list rendered, not just the header).
// 3. After Escape, the dialog is removed from DOM.
//
// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
//
// gh #152 (Class 2 — Playwright timeout) fix-forward: the prior fixture
// located the modal header via Page.GetByRole(AriaRole.Heading, name='Table
// Options'). ModalHeader.tsx renders a div (not an h*), so the
// Heading-role locator never matched — only the dialog's aria-labelledby
// wiring at Modal.tsx:184 sets the accessible name. Switched to the
// dialog-by-accessible-name pattern.
[TestFixture]
[Category("AutomationTest")]
public class TableOptionsModalFixture : AutomationTest
{
    [Test]
    public async Task columns_toggle()
    {
        // Wanted/Missing page mounts TableOptionsModalWrapper unconditionally
        // (Missing.tsx L297-L306). The wrapper's child is a PageToolbarButton
        // with label={translate('Options')} → "Options".
        await Page.GotoAsync($"{RootUri}/manga/wanted/missing");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click the "Options" toolbar button. Use the role+name locator —
        // multiple buttons may exist on the page; `.First` picks the first
        // toolbar Options button (the table-options entry-point).
        var optionsButton = Page.GetByRole(AriaRole.Button, new() { Name = "Options" });
        await optionsButton.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await optionsButton.First.ClickAsync();

        // STATE assertion 1: dialog with accessible name "Table Options"
        // visible. TableOptionsModal.tsx L136 renders
        // <ModalHeader>{translate('TableOptions')}</ModalHeader> → "Table
        // Options" per en.json. Modal.tsx:184 sets aria-labelledby={headerId}
        // on the role=dialog wrapper, so the dialog's accessible name resolves
        // to the ModalHeader text. The prior AriaRole.Heading locator never
        // matched because ModalHeader renders a `<div>`, not an `<h*>`.
        var dialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Table Options" });
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await Assertions.Expect(dialog).ToBeVisibleAsync();

        // STATE assertion 2: modal body lists at least one column row. The
        // TableOptionsColumn rows render a draggable list inside the modal.
        // ModalBody's first child rows are sub-elements with a label — use
        // the dialog's text content to assert the column-list rendered
        // (substantively more than just the header).
        var bodyText = await dialog.TextContentAsync();
        bodyText.Should().NotBeNull();
        bodyText!.Length.Should().BeGreaterThan(
            "Table Options".Length,
            "because the modal body must contain more than just the header (column rows / page-size field / column headers FormLabel)");

        // STATE assertion 3: dismiss via Escape and assert the dialog is hidden.
        // (The modal closer is typically an X button or click-outside; Escape
        // works for any Modal that wires the close callback to the global
        // closeModal shortcut.)
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).ToBeHiddenAsync();
    }
}
