using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/ (urlBase redirect)`.
// D-04: route axis ⇒ PR-smoke.
//
// GH #174 close-out (debug session gh174-urlbase-redirect-spa-bug):
// the prior iteration of this fixture rewrote `initialize.json` via Playwright's
// route handler to inject `urlBase=/mangarr`, expecting the SPA's
// RedirectWithUrlBase <Route> to fire on initial render. That approach was
// structurally broken: webpack publicPath is set from `window.Mangarr.urlBase`,
// so once the bundle bootstraps it tries to fetch all dynamic chunks from
// `/mangarr/*` — but the backend was started WITHOUT urlBase configured, so
// every chunk 404s, React never renders, no redirect ever fires, and the page
// stays blank with the URL stuck at `/`.
//
// The correct shape, locked in by this fixture: configure the runner with a
// real UrlBase via config.xml (AutomationTest.ConfiguredUrlBase override →
// NzbDroneRunner.Start(urlBase: ...) → ConfigFileProvider.UrlBase reads the
// config XML, populates Mangarr.Http.Middleware.UrlBaseMiddleware, prepends
// UrlBase to all served HTML attribute paths, and substitutes __URL_BASE__ in
// index.ejs). Then navigate to the *unprefixed* root `/` and assert the
// backend's UrlBaseMiddleware issues a 307 redirect to `/{urlBase}/`. The SPA
// also carries a defensive in-app redirect (AppRoutes.tsx — redirect Route
// hoisted above MangaIndex Route per GH #174), which acts as a belt-and-braces
// guard if the request ever reaches the SPA with PathBase stripped by an
// upstream proxy.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class UrlBaseRedirectFixture : AutomationTest
{
    private const string TestUrlBase = "mangarr";

    protected override string ConfiguredUrlBase => TestUrlBase;

    [Test]
    public async Task redirects_when_urlbase_set()
    {
        // OneTimeSetUp already navigated to HostBaseUrl ($"{RootUri}/{TestUrlBase}")
        // and waited for the app shell. Sanity-check the post-boot URL is under
        // /mangarr so a regression in OneTimeSetUp surfaces here distinctly from
        // a redirect failure.
        Page.Url.Should().Contain($"/{TestUrlBase}", "post-boot URL should already be under the configured urlBase");

        // The actual assertion: an explicit navigation to the unprefixed root
        // `/` must end up under `/{urlBase}` via either backend 307 or SPA
        // redirect. WaitForFunctionAsync polls window.location.pathname (does
        // NOT depend on a Playwright navigation event firing — react-router
        // history.push for the SPA-side redirect doesn't emit a Load event).
        await Page.GotoAsync(
            $"{RootUri}/",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });

        await Page.WaitForFunctionAsync(
            $"() => window.location.pathname.includes('/{TestUrlBase}')",
            null,
            new PageWaitForFunctionOptions { Timeout = 25_000, PollingInterval = 200 });

        Page.Url.Should().Contain($"/{TestUrlBase}");

        // Also assert that window.Mangarr.urlBase has been correctly injected by
        // HtmlMapperBase.GetHtmlText's __URL_BASE__ substitution. This catches
        // the case where the redirect fires but the SPA bootstrap is mis-wired.
        var urlBaseFromWindow = await Page.EvaluateAsync<string>(
            "() => (window.Mangarr && window.Mangarr.urlBase) || ''");
        urlBaseFromWindow.Should().Be($"/{TestUrlBase}");
    }
}
