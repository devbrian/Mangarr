using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 1 — Tests/Global/ terminology audit (req DOMAIN-01,
/// INVENTORY row 49). Walks a small, representative route set and asserts the
/// visible-DOM text contains none of the Sonarr-shape user-facing terms.
/// Case-sensitive match — Mangarr-rebranded text like "Manga"/"Chapter" are
/// the canonical replacements; lowercase `sonarr.tv` is preserved on /system/status
/// as an upstream-attribution link per D-06 (Phase 7 close-out) and is NOT in
/// scope here (the audit is case-sensitive against capitalized noun forms).
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
/// body.TextContentAsync().Should().NotContain("Series") for each token × each
/// route. The audit-test-assertions.sh gate recognizes `.Should().NotContain`
/// as a state predicate.
///
/// Cross-process AddManga seed dependency: NONE. This fixture is fully live —
/// it navigates pure-read routes; no AddMangaFlow.AddByMangaBakaIdAsync hop is
/// required (issue #102 D-D race does not apply).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class TerminologyAuditFixture : AutomationTest
{
    private static readonly string[] Routes =
    {
        "/",
        "/calendar",
        "/manga/activity/queue",
        "/settings",
        "/system/status",
    };

    private static readonly string[] ForbiddenTokens =
    {
        "Series",
        "Episode",
        "Season",
        "TV Show",
    };

    [Test]
    public async Task no_sonarr_terms_in_visible_dom()
    {
        foreach (var route in Routes)
        {
            await Page.GotoAsync($"{RootUri}{route}");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var bodyText = await Page.Locator("body").TextContentAsync();
            bodyText.Should().NotBeNull($"because route {route} must render a populated <body>");

            foreach (var token in ForbiddenTokens)
            {
                bodyText.Should().NotContain(
                    token,
                    $"because the rebranded {route} route must not surface the Sonarr-shape term '{token}' in user-visible text");
            }
        }
    }
}
