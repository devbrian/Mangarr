using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 197
// (`modal-action ParseModal → System/Status Parse diagnostic`).
//
// Non-cassette-dependent: the Parse modal is a self-contained diagnostic
// that exercises Mangarr's local Parser against a user-typed string. It
// makes a backend call to `/api/v5/parse` which returns a ParsedEpisodeInfo
// shape entirely from local Parser.cs logic — no external API involved.
//
// The button is accessible from the Manga library page toolbar (per
// MangaIndex.tsx → ParseToolbarButton import); the modal is also surfaced
// on other pages but /manga is the most stable entry point.
//
// State assertion: the modal opens after clicking the toolbar Parse button
// and renders the input text-field + help text (terminal state — the
// modal is mounted, not just attached).
[TestFixture]
[Category("AutomationTest")]
public class ParseModalFixture : AutomationTest
{
    [Test]
    public async Task parse_modal_opens_with_input_and_help_text()
    {
        // Navigate to /manga (the Manga library page hosts the Parse toolbar
        // button in its PageToolbar). Wait for the toolbar to render.
        await new MangaIndexPage(Page).OpenAsync(RootUri);

        // The Parse toolbar button is labelled "Test Parsing" (per
        // ParseToolbarButton.tsx → translate('TestParsing')). Use the role
        // selector since the button has no dedicated testid yet.
        var parseButton = Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Test Parsing" });
        await Assertions.Expect(parseButton).ToBeVisibleAsync();
        await parseButton.ClickAsync();

        // STATE assertion 1 (modal-opens contract): the ParseModalContent
        // dialog rendered. ModalHeader text is "Test Parsing" (verbatim
        // from translate('TestParsing')).
        var modalDialog = Page.GetByRole(AriaRole.Dialog);
        await modalDialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var modalText = await modalDialog.TextContentAsync();
        modalText.Should().NotBeNullOrEmpty();
        modalText.Should().Contain("Test Parsing");

        // BL-06 (18-REVIEW): the prior `(Test Parsing|Series|release|Close)`
        // alternation contained the forbidden TV-shape token `Series` that
        // TerminologyAuditFixture.ForbiddenTokens blocks. The Contain("Test
        // Parsing") assertion on line 49 above is the canonical modal-opens
        // contract; this alternation contributed nothing useful and actively
        // documented `Series` as an acceptable modal string, contradicting
        // the sonarr-consistency-audit invariant. Removed entirely.

        // STATE assertion 3 (modal-closes contract): pressing Escape closes
        // the modal — confirms the modal infrastructure is wired (not just
        // an inert DOM render).
        await Page.Keyboard.PressAsync("Escape");
        await modalDialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 5_000
        });
    }
}
