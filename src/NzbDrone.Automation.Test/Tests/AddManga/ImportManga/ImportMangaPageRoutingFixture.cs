using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.AddManga.ImportManga;

/// <summary>
/// Phase 25.1 Plan 25.1-02 — RR v5 inner-Switch exact-prop ordering regression
/// (L-RR5-EXACT-ORDERING per 25.1-RESEARCH §9).
///
/// Wave 0 RED stub per Nyquist validation policy (VALIDATION.md). Real Playwright
/// body lands in Task 8 once Tasks 6 + 7 ship <c>ImportMangaPage</c> and swap
/// the AppRoutes route.
///
/// Canonical test method (NON-NEGOTIABLE per VALIDATION.md):
///   1. rootFolderId_path_does_not_match_selector
///      — Hitting /add/import/123 must NOT render ImportMangaSelectFolder. The
///        inner Switch's exact={true} prop on /add/import is load-bearing —
///        without it, RR v5 first-match-wins ordering would shadow the
///        :rootFolderId child route and the selector would render at any
///        nested path under /add/import.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp. Tier: PRSmoke (routing-shadow
/// regression — any regression here breaks the entire library-import flow).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class ImportMangaPageRoutingFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
    }

    [Test]
    public async Task rootFolderId_path_does_not_match_selector()
    {
        Assert.Inconclusive(
            "Wave 0 stub — implementation pending Plan 25.1-02 Task 8 (covers L-RR5-EXACT-ORDERING — exact-prop on inner /add/import route prevents shadowing of the /:rootFolderId nested route once Tasks 6 + 7 land).");
        await Task.CompletedTask;
    }
}
