using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — MetadataSource list-load fixture.
/// Greens INVENTORY v5-endpoint row
/// `GET /api/v5/metadatasource | Settings/MetadataSource list`.
///
/// Blocker #4 mitigation: seeds a MetadataSource via TestKit.SeedMetadataSourceAsync
/// so the list is guaranteed non-empty (MangaDex baseline is registered as a
/// schema option but no row is auto-seeded — fresh-DB-per-fixture per Phase 18 D-05).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MetadataSourceListFixture : AutomationTest
{
    private const string SeedName = "Plan 20-07b MS List";

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedMetadataSourceAsync(SeedName);
    }

    [Test]
    public async Task list_loads()
    {
        var page = await new SettingsMetadataSourcePage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var listTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/metadatasource") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });
        await Page.ReloadAsync();
        var resp = await listTask;

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain(SeedName);
    }
}
