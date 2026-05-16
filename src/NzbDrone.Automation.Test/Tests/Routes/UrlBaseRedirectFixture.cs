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

        // PR #173 CI-fix (2026-05-15 iteration 2):
        // Iteration 1 tried WaitUntil=NetworkIdle on Goto — that timed out at 30s.
        // SignalR keeps the network continuously busy (no "idle" state is ever
        // reached), so NetworkIdle is a dead-end for any Mangarr SPA navigation.
        // Use WaitUntil=DOMContentLoaded which fires after the initial HTML parses
        // but does NOT wait for all subresources — this lets the SPA's dynamic
        // import('./bootstrap') and the intercepted initialize.json fetch resolve
        // afterwards, and the subsequent WaitForURLAsync polls for the
        // RedirectWithUrlBase route to fire (history.push, client-side).
        await Page.GotoAsync(
            $"{RootUri}/",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });

        // STATE assertion (allowlist token .Should().Contain): URL contains the
        // urlBase prefix after the redirect settles. WaitForURLAsync polls the
        // location continually, giving the React tree time to mount, bootstrap,
        // and execute the Redirect.
        await Page.WaitForURLAsync(url => url.Contains("/mangarr"), new PageWaitForURLOptions { Timeout = 30_000 });
        Page.Url.Should().Contain("/mangarr");
    }
}
