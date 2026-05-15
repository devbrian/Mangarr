using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — CustomFormat bulk-delete fixture.
/// Greens INVENTORY v5-endpoint row `DELETE /api/v5/customformat/bulk |
/// Settings/CustomFormats bulk-delete`.
///
/// Blocker #4 mitigation: seeds 2 CustomFormats then deletes them in one
/// bulk-DELETE. State-assertion = GET response no longer contains either
/// name.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class CustomFormatBulkDeleteFixture : AutomationTest
{
    [Test]
    public async Task bulk_delete()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var name1 = $"Plan 20-07a BulkDel1 {Guid.NewGuid():N}".Substring(0, 30);
        var name2 = $"Plan 20-07a BulkDel2 {Guid.NewGuid():N}".Substring(0, 30);
        var id1 = await tk.SeedCustomFormatAsync(name1);
        var id2 = await tk.SeedCustomFormatAsync(name2);
        id1.Should().BeGreaterThan(0);
        id2.Should().BeGreaterThan(0);

        // Bulk DELETE — remove both rows in one call.
        var bulkUrl = $"{RootUri}/api/v5/customformat/bulk";
        var delResp = await Page.APIRequest.FetchAsync(bulkUrl, new APIRequestContextOptions
        {
            Method = "DELETE",
            DataObject = new
            {
                ids = new[] { id1, id2 }
            }
        });
        delResp.Status.Should().BeInRange(200, 299);

        // State assertion: GET surface no longer contains either name.
        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/customformat");
        listResp.Status.Should().Be(200);

        var body = await listResp.TextAsync();
        body.Should().NotContain(name1);
        body.Should().NotContain(name2);
    }
}
