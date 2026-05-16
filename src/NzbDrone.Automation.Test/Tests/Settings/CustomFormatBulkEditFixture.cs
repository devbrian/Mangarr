using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — CustomFormat bulk-edit fixture.
/// Greens INVENTORY v5-endpoint row `PUT /api/v5/customformat/bulk |
/// Settings/CustomFormats bulk-edit`.
///
/// Blocker #4 mitigation: seeds 2 CustomFormats so the bulk payload has IDs
/// to operate on. Issues a PUT with both IDs and the
/// <c>IncludeCustomFormatWhenRenaming = true</c> flag flipped; state-assertion
/// reads back via GET and verifies the value persisted on each row.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class CustomFormatBulkEditFixture : AutomationTest
{
    [Test]
    public async Task bulk_edit()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var id1 = await tk.SeedCustomFormatAsync($"Plan 20-07a Bulk1 {Guid.NewGuid():N}".Substring(0, 30));
        var id2 = await tk.SeedCustomFormatAsync($"Plan 20-07a Bulk2 {Guid.NewGuid():N}".Substring(0, 30));
        id1.Should().BeGreaterThan(0);
        id2.Should().BeGreaterThan(0);

        // Bulk PUT — flip IncludeCustomFormatWhenRenaming on both rows.
        var bulkUrl = $"{RootUri}/api/v5/customformat/bulk";
        var putResp = await Page.APIRequest.FetchAsync(bulkUrl, new APIRequestContextOptions
        {
            Method = "PUT",
            DataObject = new
            {
                ids = new[] { id1, id2 },
                includeCustomFormatWhenRenaming = true
            }
        });
        putResp.Status.Should().BeInRange(200, 299);

        // State assertion: GET surface confirms the flag is now true on both rows.
        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/customformat");
        listResp.Status.Should().Be(200);

        var body = await listResp.TextAsync();
        body.Should().Contain($"\"id\":{id1}");
        body.Should().Contain($"\"id\":{id2}");
        body.Should().Contain("\"includeCustomFormatWhenRenaming\":true");
    }
}
