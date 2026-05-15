using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — CustomFilters list endpoint.
/// Greens INVENTORY v5-endpoint row
/// `GET /api/v5/customfilter | CustomFilters dropdown loader`.
///
/// /api/v5/customfilter is hit by every CustomFilter dropdown on app pages
/// (MangaIndex, Activity tables, Wanted tables). Empty list is a valid response
/// shape for a fresh-DB fixture — the deterministic assertion is the 200 status
/// + JSON array shape, mirroring the Plan 20-07a CustomFormatListFixture pattern.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class CustomFiltersDropdownFixture : AutomationTest
{
    [Test]
    public async Task dropdown_loads()
    {
        await Page.GotoAsync(RootUri);

        var resp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/customfilter");
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().StartWith("[");
    }
}
