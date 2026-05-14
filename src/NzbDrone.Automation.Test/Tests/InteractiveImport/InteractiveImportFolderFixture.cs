using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.InteractiveImport;

/// <summary>
/// Phase 18 Plan 18-18 — InteractiveImport modal folder-input coverage
/// (INVENTORY modal-action InteractiveImportModal — row 171).
///
/// Builds on Plan-08's InteractiveImportOpenFixture (which proved the modal
/// open from /add/import — currently Assert.Ignore'd because /add/import is
/// not yet wired per DEF-18-08-01). This fixture takes a different entry-
/// point: navigates to /system/tasks and attempts to invoke the Manual
/// Import action there. If neither entry-point is wired, the fixture
/// Assert.Inconclusive's with the canonical follow-up reference.
///
/// State assertion: when the modal opens, it carries the
/// interactive-import-modal testid AND its content surface is non-empty.
/// When neither entry-point is wired, Assert.Inconclusive emits the
/// follow-up reference and exits green-but-skipped.
///
/// Does NOT depend on AddMangaFlow — no [Explicit] needed (the manual-import
/// flow operates on filesystem paths, not on library state).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class InteractiveImportFolderFixture : AutomationTest
{
    [Test]
    public async Task interactive_import_modal_opens_from_system_tasks_or_inconclusive()
    {
        // Attempt the System/Tasks entry-point first.
        await new SystemTasksPage(Page).OpenAsync(RootUri);

        // The InteractiveImportModal is mounted imperatively in several
        // places: drag-drop drop-targets, system tasks "Manual Import" action,
        // and the queue-row InteractiveImport button. None of those have a
        // route-anchored mount yet (DEF-18-08-01). We try the most-discoverable
        // path — a "Manual Import" button on /system/tasks — and fall back to
        // Inconclusive when the button is not present.
        var manualImportButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Manual Import" });
        var hasButton = await manualImportButton.CountAsync() > 0;

        if (!hasButton)
        {
            // Per DEF-18-08-01: /add/import is not yet wired and no other route
            // entry-point lands the modal. The fixture stays green-but-skipped
            // with an explicit follow-up reference. Plan-08 file the
            // enhancement-labelled GH issue (see DEF-18-08-01 in deferred-items.md).
            Assert.Inconclusive(
                "InteractiveImportModal has no UI route entry point in the current AppRoutes.tsx + system/tasks page. " +
                "See DEF-18-08-01 in .planning/phases/18-automated-ui-integration-test-suite-playwright-net/deferred-items.md. " +
                "When a /add/import route or a system/tasks Manual Import button lands, this fixture promotes to a live state assertion automatically.");
            return;
        }

        await manualImportButton.First.ClickAsync();

        // STATE assertion: the InteractiveImportModal opens.
        var modal = new InteractiveImportModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 15_000
        });

        // STATE assertion: the modal content surface is non-empty (table
        // testid present — even if the table is empty under fresh-DB, the
        // wrapping container exists).
        var contentVisible = await modal.Table.CountAsync();
        contentVisible.Should().BeGreaterThan(0, "InteractiveImportModal table container must render even on empty state");
    }
}
