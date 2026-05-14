using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 1 — Tests/Global/ palette presence (req UI-02,
/// INVENTORY row 69). Verifies the rebranded manga-pink palette is present in
/// the served logo SVG. Per the Phase 15 D-A close-out, Mangarr retinted
/// Sonarr's logo.svg in place — cyan (#0CF) → manga pink (#f06292) — keeping
/// the same geometry / path / file (frontend/src/Components/Page/Header/PageHeader.tsx
/// L46-L51 records the divergence).
///
/// State assertion: the served /Content/Images/logo.svg payload contains the
/// canonical manga-pink hex literal AND does NOT contain the Sonarr cyan hex.
/// Both are direct STATE predicates (.Should().Contain / .Should().NotContain).
///
/// Cross-process AddManga seed dependency: NONE. The fixture issues a single
/// HTTP GET against /Content/Images/logo.svg; no library state, no modal, no
/// AddMangaFlow hop. Issue #102 D-D race does not apply.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class PalettePresenceFixture : AutomationTest
{
    // Canonical manga-pink hex (PageHeader.tsx L48; logo.svg `fill="#f06292"`
    // on the path / stroke definitions inside the SVG).
    private const string MangaPinkHex = "#f06292";

    // Sonarr cyan accent that was retinted away — must NOT appear in the
    // rebranded logo body.
    private const string SonarrCyanHex = "#0CF";

    [Test]
    public async Task manga_pink_logo()
    {
        // The page header carries the served logo URL — boot the page so that
        // Mangarr.Http frontend mapper has wired the /Content/Images/* route,
        // then fetch the SVG directly. Using the same HTTP client the browser
        // would use exercises the real served-asset surface.
        await Page.GotoAsync($"{RootUri}/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        var svg = await http.GetStringAsync($"{RootUri}/Content/Images/logo.svg");

        // STATE assertion 1: payload non-empty (no 404 + empty body silent failure).
        svg.Should().NotBeNullOrEmpty();

        // STATE assertion 2: canonical manga-pink hex is present.
        svg.Should().Contain(
            MangaPinkHex,
            "because logo.svg must carry the rebranded manga-pink palette (PageHeader.tsx L46-L51)");

        // STATE assertion 3: Sonarr cyan accent was retinted away.
        svg.Should().NotContain(
            SonarrCyanHex,
            "because the Sonarr cyan accent (#0CF) was retinted to manga pink in the Phase 15 D-A close-out");
    }
}
