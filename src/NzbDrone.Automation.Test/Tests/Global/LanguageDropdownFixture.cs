using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — Language list endpoint.
/// Greens INVENTORY v5-endpoint row
/// `GET /api/v5/language | Language dropdowns`.
///
/// The /api/v5/language endpoint feeds Language-aware dropdowns (e.g. UI
/// language picker, TranslationProfile language selection). API-driven via
/// Page.APIRequest — the dropdown is a downstream consumer of the same
/// canonical surface.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class LanguageDropdownFixture : AutomationTest
{
    [Test]
    public async Task language_dropdown_loads()
    {
        // Open the app shell so the request context is wired (ExtraHTTPHeaders).
        await Page.GotoAsync(RootUri);

        var resp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/language");
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().StartWith("[");

        // Languages list must include at least the baseline English entry.
        body.Should().Contain("English");
    }
}
