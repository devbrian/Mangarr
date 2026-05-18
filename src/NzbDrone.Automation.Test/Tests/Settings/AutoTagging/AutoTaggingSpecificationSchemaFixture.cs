using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.AutoTagging;

/// <summary>
/// Phase 24 Plan 24-05 — AutoTagging schema-endpoint live fixture. Greens
/// INVENTORY v5-endpoint row `GET /api/v5/autotagging/schema | Settings/Tags/
/// Auto Tagging Add rule spec-type dropdown`.
///
/// Asserts GET /api/v5/autotagging/schema returns 200 + a JSON array of 11
/// spec types (per 24-PLAN-DECISIONS.md §Spec Catalog Plan Map derivation:
/// 8 Sonarr ports - 1 OriginalLanguage drop + 1 QualityProfile split-add +
/// 3 manga-NEW = 11). The unit-level pin lives at NzbDrone.Api.Test/
/// AutoTagging/AutoTaggingSpecificationSchemaFixture.cs (Plan 24-04); this
/// fixture is the LIVE end-to-end equivalent against the running app.
///
/// Open Q #3 honored — the schema endpoint is inlined on
/// AutoTaggingController as [HttpGet("schema")]; there is NO separate
/// AutoTaggingSpecificationController.cs file.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class AutoTaggingSpecificationSchemaFixture : AutomationTest
{
    [Test]
    public async Task schema_returns_eleven_specs()
    {
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Microsoft.Playwright.Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var schemaResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/autotagging/schema");
        schemaResp.Status.Should().Be(200,
            "GET /api/v5/autotagging/schema should return 200 (inlined endpoint per Open Q #3)");

        var schemaJson = await schemaResp.JsonAsync();
        schemaJson.HasValue.Should().BeTrue();
        var root = schemaJson!.Value;
        root.ValueKind.Should().Be(global::System.Text.Json.JsonValueKind.Array);

        // Spec total pinned at 11 (literal in both CatalogFixture +
        // DryIocAutoDiscoveryFixture per 24-03 SUMMARY decisions).
        var implementations = new List<string>();
        foreach (var element in root.EnumerateArray())
        {
            element.TryGetProperty("implementation", out var impl).Should().BeTrue();
            implementations.Add(impl.GetString() ?? string.Empty);
        }

        implementations.Should().HaveCount(11,
            "11-spec catalog per AT-03 + AT-04 + AT-05 + Open Q #1 net derivation");

        // Spot-check that the 3 manga-NEW specs are present (AT-04).
        implementations.Should().Contain("AuthorArtistSpecification");
        implementations.Should().Contain("DemographicSpecification");
        implementations.Should().Contain("ContentRatingSpecification");

        // Spot-check that the 4 dropped specs are absent (AT-05 + Open Q #1).
        implementations.Should().NotContain("NetworkSpecification");
        implementations.Should().NotContain("SeriesTypeSpecification");
        implementations.Should().NotContain("OriginalCountrySpecification");
        implementations.Should().NotContain("OriginalLanguageSpecification");

        // Spot-check that the QualityProfile split-add yielded 2 specs (AT-03).
        implementations.Should().Contain("TranslationProfileSpecification");
        implementations.Should().Contain("CustomFormatProfileSpecification");
    }
}
