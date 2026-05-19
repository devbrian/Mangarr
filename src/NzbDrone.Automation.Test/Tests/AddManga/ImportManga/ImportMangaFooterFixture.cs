using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.AddManga.ImportManga;

/// <summary>
/// Phase 25.1 Plan 25.1-02 — bulk-apply toolbar coverage (D-05).
///
/// Wave 0 RED stubs per Nyquist validation policy (VALIDATION.md). Real
/// Playwright assertion bodies land in Task 8 once Task 5 ships
/// <c>ImportMangaFooter.tsx</c> (the bulk-apply toolbar).
///
/// Canonical test method (NON-NEGOTIABLE per VALIDATION.md):
///   1. bulk_set_monitor_applies_to_selected — II-10 D-05 bulk-apply Monitor
///
/// Pitfall 10: Comix disabled in OneTimeSetUp. Tier: PRSmoke.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class ImportMangaFooterFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
    }

    [Test]
    public async Task bulk_set_monitor_applies_to_selected()
    {
        Assert.Inconclusive(
            "Wave 0 stub — implementation pending Plan 25.1-02 Task 8 (covers Task 5 ImportMangaFooter bulk-apply Monitor → All updating each selected row's monitor field in importMangaStore).");
        await Task.CompletedTask;
    }
}
