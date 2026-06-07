using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY v5-endpoint row
/// `POST /api/v5/command` (UI-triggered command run).
///
/// Flow: AddMangaFlow seed → MangaDetails Refresh button → clicking Refresh
/// enqueues a RefreshManga command via POST /api/v5/command (per MangaDetails
/// toolbar wiring).
///
/// Per D-04 row-axis: v5-endpoint → PRSmoke for GET-heavy, but POST is the
/// command-RUN write-path. Plan 20-08 acceptance criteria specifies Nightly
/// for CommandRun (modal-action axis-leaning per the destructive-ish write).
///
/// Blocker #4: 1 manga seeded upfront; zero inconclusive-skip branches.
/// Pitfall 10: Comix disabled.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class CommandRunFixture : AutomationTest
{
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    [Test]
    public async Task ui_button_pushes_command()
    {
        var details = await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);
        await Assertions.Expect(details.MainContainer).ToBeVisibleAsync();

        // Race a POST /api/v5/command on the Refresh button click — that's the
        // canonical UI-triggered command path (the RefreshManga command is
        // enqueued via useCommands.runCommand → POST /api/v5/command).
        var cmdTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/command") && r.Request.Method == "POST",
            new PageWaitForResponseOptions { Timeout = 30_000 });

        await details.RefreshButton.ClickAsync();

        var resp = await cmdTask;
        resp.Status.Should().BeInRange(
            200,
            299,
            "POST /api/v5/command must return 2xx (UI-triggered command run contract)");
        resp.Url.Should().Contain("/api/v5/command",
            "request URL must hit the command endpoint precisely");
    }
}
