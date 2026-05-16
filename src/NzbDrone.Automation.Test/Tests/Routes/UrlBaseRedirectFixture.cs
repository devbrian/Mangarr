using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/ (urlBase redirect)` —
// RedirectWithUrlBase fires when window.Mangarr.urlBase is non-empty.
// D-04: route axis ⇒ PR-smoke.
//
// Mechanism: window.Mangarr is populated from a runtime fetch of /initialize.json
// (frontend/src/index.ts:5-10). AddInitScriptAsync cannot influence that — the
// fetched JSON overwrites the window object. Instead, intercept the initialize.json
// response with Playwright's route handler and inject urlBase=/mangarr. AppRoutes.tsx
// (lines 59-68) then renders the RedirectWithUrlBase <Route> branch, which redirects
// / to /mangarr.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class UrlBaseRedirectFixture : AutomationTest
{
    [Test]
    public async Task redirects_when_urlbase_set()
    {
        // Intercept the initialize.json fetch and inject urlBase. Fetch the original
        // body first so other fields (apiKey, version, etc.) survive unchanged; only
        // the urlBase field is overridden. Registered BEFORE the navigation that
        // triggers the fetch.
        await Page.RouteAsync("**/initialize.json**", async route =>
        {
            var response = await route.FetchAsync();
            var originalBody = await response.TextAsync();
            using var doc = JsonDocument.Parse(originalBody);
            var root = doc.RootElement;

            // Re-emit JSON with urlBase overridden.
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();

                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals("urlBase"))
                    {
                        writer.WriteString("urlBase", "/mangarr");
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }

                // If urlBase wasn't present, add it now.
                if (!root.TryGetProperty("urlBase", out _))
                {
                    writer.WriteString("urlBase", "/mangarr");
                }

                writer.WriteEndObject();
            }

            var patched = Encoding.UTF8.GetString(ms.ToArray());

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = (int)response.Status,
                ContentType = "application/json",
                Body = patched,
            });
        });

        // PR #173 CI-fix (2026-05-15 iteration 3): see GH issue #174 for the
        // production SPA bug this fixture surfaces (route-declaration-order
        // in frontend/src/App/AppRoutes.tsx). The fixture stays here as the
        // authoritative failure marker; when the SPA bug is fixed, the
        // WaitForFunctionAsync poll below turns green unchanged.
        //
        // Iteration 2 tried WaitUntil=DOMContentLoaded; still timed out on the
        // subsequent WaitForURLAsync (Playwright defaults to waiting for a Load
        // navigation event, which never fires for a React Router history.push
        // soft navigation in this SPA shape).
        //
        // ROOT CAUSE (verified by reading AppRoutes.tsx:57-68 + Switch.tsx):
        // The redirect Route is rendered SECOND in the Switch, after the
        // unconditional MangaIndex Route. React Router v5 <Switch> uses
        // first-match-wins; at initial render `window.Mangarr.urlBase` is empty,
        // so Switch's wrapper prepends nothing — MangaIndex matches "/" and
        // renders. After the intercepted initialize.json sets urlBase=/mangarr,
        // React re-renders Switch — but MangaIndex still matches because the
        // browser URL is still "/" (the route ordering means the redirect Route
        // never gets a chance to run). This is a PRODUCTION bug in the SPA
        // route declaration order, NOT a fixture bug.
        //
        // Per PR #173 task constraints: production code is OFF-LIMITS in this
        // iteration. Filing a follow-up issue (label: bug, test) for the SPA
        // route-ordering fix; this fixture stays in PRSmoke as an authoritative
        // failure marker on the bug. The contract assertion is preserved
        // verbatim — when the SPA bug is fixed, this fixture will turn green
        // unchanged.
        //
        // The expected production fix: in AppRoutes.tsx, hoist the conditional
        // redirect Route ABOVE the unconditional MangaIndex Route when
        // window.Mangarr.urlBase is non-empty, so the first-match-wins Switch
        // picks the redirect path before MangaIndex on a hosted-under-urlBase
        // deployment.
        //
        // For now, switch to a polled URL check via WaitForFunctionAsync — this
        // does NOT depend on a Playwright navigation event firing (no
        // history.push event coupling). If the SPA does redirect (after the bug
        // is fixed), the check turns green within polling cadence. If not, the
        // 25-second budget (≤ harness 30s) gives the bug headroom to be flaky
        // green if it ever races on a faster path.
        await Page.GotoAsync(
            $"{RootUri}/",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });

        await Page.WaitForFunctionAsync(
            "() => window.location.pathname.includes('/mangarr')",
            null,
            new PageWaitForFunctionOptions { Timeout = 25_000, PollingInterval = 200 });
        Page.Url.Should().Contain("/mangarr");
    }
}
