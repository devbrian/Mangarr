using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.AddManga;

/// <summary>
/// Phase 25.1 Plan 25.1-02 — Phase 25 manual-import-as-page deletion regression
/// (D-01 + L-COMMIT-CLUSTER-ATOMICITY).
///
/// Wave 0 RED stub per Nyquist validation policy (VALIDATION.md). Real
/// Playwright body lands in Task 8 once Task 7 atomically swaps AppRoutes from
/// the Phase 25 page to the new ImportMangaPage and deletes the AddImportPage
/// directory.
///
/// Canonical test method (NON-NEGOTIABLE per VALIDATION.md):
///   1. add_import_page_testid_absent
///      — Navigating to /add/import must NOT render the page-scoped testid
///        the Phase 25 manual-import-as-page emitted. It MUST render the
///        Sonarr-canonical library-import selector page in its place.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp. Tier: PRSmoke (deletion-
/// regression — surfaces immediately on any accidental Phase 25 revert).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class AddImportPageDeletionRegressionFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
    }

    [Test]
    public async Task add_import_page_testid_absent()
    {
        Assert.Inconclusive(
            "Wave 0 stub — implementation pending Plan 25.1-02 Task 8 (covers Task 7 atomic-cluster — AppRoutes swap + AddImportPage/ delete; asserts the Phase 25 page-scoped testid is absent and ImportMangaSelectFolder renders instead).");
        await Task.CompletedTask;
    }
}
