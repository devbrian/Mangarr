using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 1 — Tests/Global/ rebrand audit (req UI-01,
/// INVENTORY row 68). Asserts:
/// 1. HTML &lt;title&gt; element resolves to a Mangarr-shaped string.
/// 2. Visible-DOM body text on representative routes contains no capitalized
///    "Sonarr" substring. Note: lowercase "sonarr.tv" persists on /system/status
///    (MoreInfo.tsx upstream-attribution Link per D-06) — that's preserved on
///    purpose; the case-sensitive `NotContain("Sonarr")` does not match it.
///
/// State assertion: title.Should().Contain("Mangarr") + body.NotContain("Sonarr").
///
/// Cross-process AddManga seed dependency: NONE. Routes walked here are
/// pure-read (root, /calendar, /manga/activity/queue, /settings, /system/status);
/// issue #102 D-D race does not apply.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class RebrandAuditFixture : AutomationTest
{
    private static readonly string[] Routes =
    {
        "/",
        "/calendar",
        "/manga/activity/queue",
        "/settings",
        "/system/status",
    };

    [Test]
    public async Task title_and_strings_are_mangarr()
    {
        await Page.GotoAsync($"{RootUri}/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion: the HTML <title> must contain "Mangarr". The exact
        // value at runtime is "Mangarr" (per IndexHtmlMapper.cs / Bootstrap.cs);
        // we use .Contain to remain resilient if Phase 99 distribution adds a
        // version suffix.
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        // STATE assertion: no capitalized "Sonarr" anywhere in visible DOM text.
        // Case-sensitive — preserves the D-06 lowercase sonarr.tv link text on
        // /system/status (handled by the explicit allowlist below).
        foreach (var route in Routes)
        {
            await Page.GotoAsync($"{RootUri}{route}");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var bodyText = await Page.Locator("body").TextContentAsync();
            bodyText.Should().NotBeNull($"because route {route} must render a populated <body>");

            // Strip the lowercase sonarr.tv attribution links — those are
            // D-06-preserved upstream references, not Sonarr-branded copy.
            var auditable = bodyText!
                .Replace("sonarr.tv", string.Empty)
                .Replace("forums.sonarr.tv", string.Empty)
                .Replace("discord.sonarr.tv", string.Empty);

            auditable.Should().NotContain(
                "Sonarr",
                $"because the rebranded {route} route must not surface capitalized 'Sonarr' in user-visible product copy");
        }
    }
}
