using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `* (catch-all)` — NotFound page
// renders for invalid URLs. D-04: route axis ⇒ PR-smoke.
//
// frontend/src/Components/NotFound.tsx renders translate('DefaultNotFoundMessage')
// which resolves to "You must be lost, nothing to see here." (en.json:353).
// No data-testid exists on the NotFound page (it ships as an opaque PageContent
// surface); assert on the localized text content instead — that IS the page's
// observable rendered state and survives gracefully if a future inline-fix adds
// a testid.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class NotFoundPageFixture : AutomationTest
{
    [Test]
    public async Task renders_not_found()
    {
        // Use a uniquely-shaped URL the catch-all is guaranteed to hit; the GUID
        // prevents a stale cached SPA from short-circuiting the navigation.
        var unknown = $"/this-route-does-not-exist-{Guid.NewGuid():N}";
        await Page.GotoAsync($"{RootUri}{unknown}");

        // STATE assertion: NotFound renders the DefaultNotFoundMessage translation.
        // Case-insensitive regex tolerates a future copy tweak. ToBeVisibleAsync
        // waits for the element to render (post-bootstrap).
        var notFoundText = Page.GetByText(new Regex("nothing to see here", RegexOptions.IgnoreCase));
        await Assertions.Expect(notFoundText).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // URL should still contain the bogus path (SPA renders NotFound in place;
        // does NOT redirect to a different URL). Allowlist token: .Should().Contain.
        Page.Url.Should().Contain("this-route-does-not-exist-");
    }
}
