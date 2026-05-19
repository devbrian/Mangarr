using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.AddManga.ImportManga;

/// <summary>
/// Phase 25.1 Plan 25.1-02 — root-folder selector page coverage.
///
/// Wave 0 RED stubs per Nyquist validation policy (VALIDATION.md). Real
/// Playwright assertion bodies land in Task 8 once the Task 4
/// <c>ImportMangaSelectFolder</c> subtree is authored.
///
/// Canonical test methods (NON-NEGOTIABLE per VALIDATION.md — audit-new-fixtures.sh
/// enumerates by these names):
///   1. renders_root_folder_list           — II-01 amended + II-10 selector page renders
///   2. click_root_folder_routes_to_scan   — II-10 click navigates to /add/import/:rootFolderId
///
/// Pitfall 10: Comix disabled in OneTimeSetUp to avoid live-indexer noise during
/// page-render assertions. Tier: PRSmoke (page-rendering surface, GET-heavy).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class ImportMangaSelectFolderFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
    }

    [Test]
    public async Task renders_root_folder_list()
    {
        Assert.Inconclusive(
            "Wave 0 stub — implementation pending Plan 25.1-02 Task 8 (replaces this stub with the canonical Playwright body once Task 4 ships ImportMangaSelectFolder subtree).");
        await Task.CompletedTask;
    }

    [Test]
    public async Task click_root_folder_routes_to_scan()
    {
        Assert.Inconclusive(
            "Wave 0 stub — implementation pending Plan 25.1-02 Task 8 (replaces this stub with the canonical Playwright body once Task 4 ships ImportMangaSelectFolderRow).");
        await Task.CompletedTask;
    }
}
