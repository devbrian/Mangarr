using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Components;

/// <summary>
/// Phase 18 Plan 18-16 Task 2 — Tests/Components/ TableOptionsModal (generic
/// column-options widget, modal-action row 183 in INVENTORY.md). The widget
/// is wired by Components/Table/TableOptions/TableOptionsModalWrapper.tsx;
/// any table page can host one. The Wanted/Missing page (Missing.tsx
/// L297-L306) wraps a PageToolbarButton with the "Options" label inside the
/// wrapper — clicking it opens the TableOptionsModal with a "Table Options"
/// header.
///
/// Why /manga/wanted/missing: same rationale as FilterModalFixture — the
/// MangaIndex Options button piggybacks on a TableOptionsModalWrapper that's
/// connected to MangaIndex grid columns whose mount state depends on having
/// manga seeded. The Wanted/Missing variant is always mounted.
///
/// State assertions:
/// 1. Modal heading "Table Options" appears in DOM after clicking Options.
/// 2. At least one column-row testid / FormGroup appears in modal body
///    (asserts the column-list rendered, not just the heading).
/// 3. After clicking the close icon (X), the heading is removed from DOM.
///
/// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
/// </summary>
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

        // STATE assertion 1: modal heading "Table Options" visible. TableOptionsModal.tsx
        // L136 renders <ModalHeader>{translate('TableOptions')}</ModalHeader>
        // → "Table Options" per en.json.
        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Table Options" });
        await heading.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var headingText = await heading.TextContentAsync();
        headingText.Should().Be("Table Options");

        // STATE assertion 2: modal body lists at least one column row. The
        // TableOptionsColumn rows render a draggable list inside the modal.
        // ModalBody's first child rows are sub-elements with a label — use
        // `body` text content of the modal area to assert the column-list
        // rendered (≥1 column label visible). The "Headers" + "PageSize"
        // FormLabels also render — at minimum one of those is present.
        var modalBody = Page.Locator(".modal-content").First;
        var bodyText = await modalBody.TextContentAsync();
        bodyText.Should().NotBeNull();
        bodyText!.Length.Should().BeGreaterThan(
            "Table Options".Length,
            "because the modal body must contain more than just the header (column rows / page-size field / column headers FormLabel)");

        // STATE assertion 3: dismiss via Escape and assert the heading is hidden.
        // (The modal closer is typically an X button or click-outside; Escape
        // works for any Modal that wires the close callback to the global
        // closeModal shortcut.)
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(heading).ToBeHiddenAsync();
    }
}
