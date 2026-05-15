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

        // Navigate to the bare root; the RedirectWithUrlBase route should fire.
        await Page.GotoAsync($"{RootUri}/");

        // STATE assertion (allowlist token .Should().Contain): URL contains the
        // urlBase prefix after the redirect settles. WaitForURLAsync allows the
        // SPA bootstrap + redirect to complete.
        await Page.WaitForURLAsync(url => url.Contains("/mangarr"), new PageWaitForURLOptions { Timeout = 15_000 });
        Page.Url.Should().Contain("/mangarr");
    }
}
