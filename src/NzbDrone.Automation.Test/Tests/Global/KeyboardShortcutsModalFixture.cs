using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 2 — Tests/Global/ KeyboardShortcutsModal
/// (modal-action row 184 in INVENTORY.md). PageHeader.tsx (L33-L41) binds
/// the `?` key via Mousetrap → opens KeyboardShortcutsModal.
///
/// State assertions:
/// 1. Modal heading "Keyboard Shortcuts" appears in DOM after the `?` keypress.
/// 2. Modal content contains the shortcut entry text from
///    Helpers/Hooks/useKeyboardShortcuts.ts (e.g. translate key
///    "KeyboardShortcutsOpenModal" resolves to a visible row).
/// 3. After pressing Escape, the modal heading is no longer visible.
///
/// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class KeyboardShortcutsModalFixture : AutomationTest
{
    [Test]
    public async Task shortcuts_render()
    {
        await Page.GotoAsync($"{RootUri}/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Press `?` to trigger Mousetrap → setIsKeyboardShortcutsModalOpen(true)
        // (PageHeader.tsx L24-L26 + L33-L41).
        await Page.Keyboard.PressAsync("Shift+/");

        // STATE assertion 1: modal heading "Keyboard Shortcuts" is visible.
        // Modal renders via Modal/ModalContent/ModalHeader chain (Components/Modal/*);
        // the heading text is the rendered i18n value "Keyboard Shortcuts" per
        // en.json. Use GetByText (a content-based locator that's stable across
        // CSS-only restyles) and assert it has visible text content.
        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Keyboard Shortcuts" });
        await heading.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        var headingText = await heading.TextContentAsync();
        headingText.Should().Be("Keyboard Shortcuts");

        // WR-10 (18-REVIEW): the prior assertion ran against
        // Page.Locator("body").TextContentAsync() — `?` appears in many
        // places on a typical page (help links, FAQ prompts, etc.) and the
        // contain-check was trivially-true regardless of modal state.
        // Scope to .modal-content so the assertion actually reflects modal
        // body content, not the whole document.
        var modalContent = Page.Locator(".modal-content").First;
        var modalText = await modalContent.TextContentAsync();
        modalText.Should().NotBeNull();
        modalText!.Should().Contain("?", "the keyboard-shortcuts modal must enumerate the `?` shortcut entry");

        // STATE assertion 3: Escape dismisses the modal — the heading should
        // no longer be in the DOM. Use ToBeHiddenAsync to assert removal.
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(heading).ToBeHiddenAsync();
    }
}
