using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 PRSmoke) — CustomFormat specification schema
/// fixture. Greens INVENTORY v5-endpoint row `GET /api/v5/customformat/schema |
/// Settings/CustomFormats spec chooser`.
///
/// Hits the schema endpoint directly via Page.APIRequest (inherits X-Api-Key
/// from the browser context). State-assertion = response 200 + body
/// contains the canonical spec implementation names (e.g.
/// <c>SourceSpecification</c>, <c>LanguageSpecification</c>).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class CustomFormatSpecSchemaFixture : AutomationTest
{
    [Test]
    public async Task schema_renders()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var schemaResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/customformat/schema");
        schemaResp.Status.Should().Be(200);

        var body = await schemaResp.TextAsync();

        // The schema endpoint returns a JSON array of specification
        // implementations. The body should start with '[' and contain at least
        // one canonical spec name (LanguageSpecification ships in
        // src/NzbDrone.Core/CustomFormats/Specifications/).
        body.Should().StartWith("[");
        body.Should().Contain("Specification");
    }
}
