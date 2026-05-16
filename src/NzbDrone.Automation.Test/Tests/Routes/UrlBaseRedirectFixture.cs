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

        // PR #173 CI-fix (2026-05-15): on Linux CI the previous Goto + WaitForURLAsync
        // sequence timed out at 15s. Two contributing factors:
        //   (1) Goto's default WaitUntil = `Load`, which fires before the SPA has
        //       finished bootstrapping (the dynamic `import('./bootstrap')` at
        //       frontend/src/index.ts:43 still resolving) — `window.Mangarr.urlBase`
        //       isn't populated yet when `Load` fires, and React's `<Switch>` initial
        //       render only sees the empty default.
        //   (2) The intercepted `initialize.json` fetch is the gate that flips
        //       `window.Mangarr.urlBase` from "" -> "/mangarr"; the redirect Route
        //       only renders AFTER that fetch resolves and React re-renders.
        // Use WaitUntil=NetworkIdle on the Goto so the SPA bootstrap + the intercepted
        // initialize.json fetch both settle before we probe the URL. Bump the
        // redirect timeout to 30s — Linux CI runners under load see SPA-bootstrap
        // tails near 15s. Test intent (urlBase prefix appears in URL after redirect)
        // preserved verbatim.
        await Page.GotoAsync(
            $"{RootUri}/",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30_000 });

        // STATE assertion (allowlist token .Should().Contain): URL contains the
        // urlBase prefix after the redirect settles.
        await Page.WaitForURLAsync(url => url.Contains("/mangarr"), new PageWaitForURLOptions { Timeout = 30_000 });
        Page.Url.Should().Contain("/mangarr");
    }
}
