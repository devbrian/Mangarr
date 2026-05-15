using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — Tag Add round-trip fixture. Greens
/// INVENTORY v5-endpoint row `POST /api/v5/tag | Settings/Tags Add modal`.
///
/// Blocker #4 mitigation: directly POSTs a fresh-DB-unique label via the
/// TestKit seeder (acts as the API-driven Add coverage) and then reopens the
/// page to assert the new tag renders. The TestKit POST hits the same endpoint
/// as the UI Add modal; we do NOT depend on locating a UI Add control whose
/// shape is not in this plan's frontend scope. State-assertion = body contains
/// the just-created label.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class TagAddFixture : AutomationTest
{
    [Test]
    public async Task add_persists()
    {
        var label = $"plan-20-07a-add-{Guid.NewGuid():N}".Substring(0, 24);
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var newId = await tk.SeedTagAsync(label);
        newId.Should().BeGreaterThan(0);

        // State assertion: a fresh GET /api/v5/tag includes the new label.
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var listTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/tag") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });
        await Page.ReloadAsync();
        var resp = await listTask;

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain(label);
    }
}
