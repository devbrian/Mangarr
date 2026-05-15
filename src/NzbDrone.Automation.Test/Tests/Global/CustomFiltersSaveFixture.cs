using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 Nightly — modal-action axis) — CustomFilters POST
/// round-trip. Greens INVENTORY v5-endpoint row
/// `POST /api/v5/customfilter | CustomFilters Save`.
///
/// API-driven: the FilterBuilder modal's Save button POSTs to /api/v5/customfilter
/// with { type, label, filters: [...] }. Mirrors the Plan 20-07a
/// ReleaseProfileCrudFixture API-CRUD pattern.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class CustomFiltersSaveFixture : AutomationTest
{
    [Test]
    public async Task save_persists()
    {
        await Page.GotoAsync(RootUri);

        var filterPayload = new
        {
            type = "series",
            label = "Plan 20-07b CF Save",
            filters = new[]
            {
                new { key = "monitored", value = new[] { true }, type = "equal" }
            }
        };

        var resp = await Page.APIRequest.PostAsync(
            $"{RootUri}/api/v5/customfilter",
            new APIRequestContextOptions { DataObject = filterPayload });

        resp.Status.Should().BeInRange(200, 299);
        var createdBody = await resp.TextAsync();
        createdBody.Should().Contain("Plan 20-07b CF Save");

        using var doc = JsonDocument.Parse(createdBody);
        var id = doc.RootElement.GetProperty("id").GetInt32();
        id.Should().BeGreaterThan(0);

        // State assertion: GET surface shows the new entry.
        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/customfilter");
        listResp.Status.Should().Be(200);
        (await listResp.TextAsync()).Should().Contain("Plan 20-07b CF Save");
    }
}
