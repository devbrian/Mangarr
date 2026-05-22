using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Chromium-free upstream-drift falsifier. Locks the structural shape of
    /// <c>ComixPuppeteerSigner.cs</c> against keiyoushi <c>Comix.kt</c>'s
    /// <c>captureToken()</c> pattern (upstream commit <c>965dc242</c>, 2026-05-12 —
    /// "Comix: only get token via webview").
    ///
    /// <para>
    /// History: the original Phase 17 shape probed <c>globalThis.vmf_*</c> namespaces
    /// for behaviour-matching signer + installer fns; upstream <c>Signer.kt</c> was
    /// deleted in commit <c>965dc242</c> (2026-05-12) and the strategy pivoted to
    /// request-interception token capture. This fixture was rewritten on 2026-05-22
    /// to lock the captureToken-shape contract — see
    /// <c>.planning/debug/comix-signer-rotation.md</c> for the root-cause + decision
    /// record. The <c>Resources/upstream-signer.txt</c> excerpt is the SHA-pinned
    /// Phase 17 port-time snapshot; it intentionally still describes the obsolete
    /// namespace-probe shape and stays unchanged as a historical breadcrumb.
    /// </para>
    /// </summary>
    [TestFixture]
    public class UpstreamSignerDriftFixture : CoreTest
    {
        private string _signerSource;
        private string _parserSource;

        [SetUp]
        public void Setup()
        {
            _signerSource = ReadCoreSource(
                TestContext.CurrentContext.TestDirectory,
                "src/NzbDrone.Core/Indexers/Comix/ComixPuppeteerSigner.cs");

            // Phase 17.2 Plan 02: also load ComixParser.cs so the GAP-17-E
            // pages-endpoint regression guard can grep against the DownloadUrl
            // construction site (where the survey-winner shape `/chapters/{id}`
            // is enforced by dropping the legacy `/pages` suffix).
            _parserSource = ReadCoreSource(
                TestContext.CurrentContext.TestDirectory,
                "src/NzbDrone.Core/Indexers/Comix/ComixParser.cs");
        }

        private static string ReadCoreSource(string startDir, string relative)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException(
                $"Could not locate '{relative}' by walking up from '{startDir}'.");
        }

        [Test]
        public void Signer_must_use_captureToken_via_request_interception()
        {
            // 2026-05-22 rotation pivot: the signer no longer probes globalThis. It
            // observes the page's own outgoing `/api/v1/...` request and extracts the
            // `_=<token>` query parameter via PuppeteerSharp's request-interception
            // event (`SetRequestInterceptionAsync(true)` + `page.Request += handler`).
            // If this assertion fails, someone has reverted to the upstream-obsolete
            // namespace-probe shape — read .planning/debug/comix-signer-rotation.md
            // for why that shape is structurally broken.
            _signerSource.Should().Contain(
                "SetRequestInterceptionAsync(true)",
                "Signer must enable request interception on the warm page so the per-call " +
                "handler can observe outgoing requests + extract `_=<token>`. See upstream " +
                "Comix.kt:414-471 captureToken() (commit 965dc242).");

            _signerSource.Should().Contain(
                "page.Request +=",
                "Signer must attach a request handler that captures the token off the page's " +
                "OWN bootstrap API call. Removing the +=/-= pair re-introduces the namespace-" +
                "probe pattern that comix.to rotated away from on 2026-05-22.");
        }

        [Test]
        public void Signer_must_NOT_probe_globalThis_namespaces()
        {
            // Negative assertion — the namespace-probe shape is upstream-obsolete (commit
            // 965dc242 deletes Signer.kt entirely). If anyone re-adds a PROBE_JS-style
            // walker, this regression guard fires verbatim.
            // Regex-based negative assertions: look for actual code usage, not historical
            // mentions in commentary. A bare PROBE_JS const declaration would match
            // `private const string PROBE_JS`; an Object.keys(window) walk would appear
            // inside an EvaluateExpressionAsync template literal between `@"` and `"`.
            _signerSource.Should().NotMatchRegex(
                @"const\s+string\s+PROBE_JS",
                "captureToken (2026-05-22 rewrite) deletes the PROBE_JS const. Re-adding " +
                "a `const string PROBE_JS = @\"...\"` regresses to the upstream-obsolete " +
                "namespace-probe pattern. See .planning/debug/comix-signer-rotation.md.");

            _signerSource.Should().NotMatchRegex(
                @"for\s*\(\s*const\s+ns\s+of\s+Object\.keys\(window\)",
                "captureToken does NOT walk window namespaces — re-adding an " +
                "`Object.keys(window)` loop regresses to the upstream-obsolete probe pattern.");
        }

        [Test]
        public void Signer_must_route_apiPath_to_one_of_two_captureToken_call_sites()
        {
            // Upstream Comix.kt has TWO captureToken call sites:
            //   - fetchChapterList: pageUrl=/title/{hid};    match /api/v1/manga/{hid}/chapters
            //   - fetchPageList:    pageUrl=/chapters/{id};  match /api/v1/chapters/{id}
            // The C# port routes via ResolveCaptureRoute. If those two shapes are gone,
            // the port has lost upstream-fidelity.
            _signerSource.Should().MatchRegex(
                @"/title/\{hid\}|""title""",
                "The /manga/{hid}/chapters route must load the title page (`/title/{hid}`) " +
                "so the bundle issues its /api/v1/manga/{hid}/chapters request. Upstream " +
                "Comix.kt:307-309.");

            _signerSource.Should().Contain(
                "/api/v1/manga/",
                "Match suffix for the chapter-list captureToken route must reference the " +
                "/api/v1/manga/ API prefix (upstream Comix.kt:310).");

            _signerSource.Should().Contain(
                "/api/v1/chapters/",
                "Match suffix for the chapter-pages captureToken route must reference the " +
                "/api/v1/chapters/ API prefix (upstream Comix.kt:390).");
        }

        [Test]
        public void Signer_must_relay_API_GET_with_captured_token_via_vanilla_http()
        {
            // After token capture, the actual API GET is relayed via a vanilla
            // System.Net.Http.HttpClient (Choice B per .planning/debug/comix-signer-rotation.md
            // Resolution.fix item 2). Page-relay (fetch / axios) returns encrypted
            // {e:<base64>} envelopes — confirmed live 2026-05-22. Vanilla HTTP matches
            // upstream Comix.kt:325 verbatim shape.
            _signerSource.Should().Contain(
                "System.Net.Http.HttpClient",
                "Signer must relay the captured-token API GET via System.Net.Http.HttpClient " +
                "(NOT via page.fetch / axios) — comix.to encrypts in-page-fetched responses but " +
                "returns plaintext to vanilla HTTP calls. Choice B per debug-doc.");

            _signerSource.Should().Contain(
                "Uri.EscapeDataString",
                "The relay URL must append the captured token as `_=<encoded>` per upstream " +
                "Comix.kt:323 (chapter list) / Comix.kt:396 (chapter pages); the C# port uses " +
                "Uri.EscapeDataString for the encoding step.");

            _signerSource.Should().Contain(
                "/api/v1",
                "The relay URL must target the /api/v1 prefix that comix.to's API serves.");
        }

        [Test]
        public void Signer_navigation_must_use_DOMContentLoaded_not_networkidle()
        {
            // The pre-2026-05-22 shape used Networkidle0 (+ a Phase 17.2 D-1 settle step).
            // The bundle's long-lived sockets defeat Networkidle0 — the page never settles.
            // DOMContentLoaded is the upstream-aligned choice (WebView's `loadUrl` fires
            // shouldInterceptRequest synchronously as the bundle bootstraps — no settle
            // needed because we're waiting on the OUTGOING request, not the page's network
            // quiescence).
            _signerSource.Should().Contain(
                "WaitUntilNavigation.DOMContentLoaded",
                "captureToken navigation must use DOMContentLoaded — the bundle's long-lived " +
                "sockets defeat Networkidle0. We wait on the captured token (the page's own " +
                "outgoing API request) rather than network quiescence.");

            // Detect actual code usage (NavigationOptions { WaitUntil = ... } / .WaitForNetworkIdleAsync(...))
            // rather than mere mentions in comments/xmldoc.
            _signerSource.Should().NotMatchRegex(
                @"WaitUntilNavigation\.Networkidle0",
                "Networkidle0 is incompatible with the captureToken shape (the bundle never " +
                "settles to network-idle). Use DOMContentLoaded + bounded tcs.Task.WaitAsync.");

            _signerSource.Should().NotMatchRegex(
                @"\.WaitForNetworkIdleAsync\(",
                "Phase 17.2 D-1's networkidle-settle step was retired alongside the namespace " +
                "probe — the captureToken shape doesn't run an in-page probe, so the settle " +
                "step is no longer needed (and would only spin for 5 seconds before timing out).");
        }

        [Test]
        public void Signer_captureToken_must_be_bounded_by_a_timeout()
        {
            // Upstream Comix.kt:466 — `latch.await(30, SECONDS)`. The C# port must
            // mirror this: a wedged page (page loaded but bundle never fires its API
            // request) cannot stall the gate indefinitely.
            _signerSource.Should().Contain(
                "CaptureTimeoutSeconds",
                "captureToken MUST carry an explicit timeout const so a wedged page (no API " +
                "request ever fires) cannot stall the gate. Upstream Comix.kt:466 uses 30s.");
        }

        [Test]
        public void Pages_endpoint_apiPath_must_match_phase_17_2_survey_winner()
        {
            // Phase 17.2 GAP-17-E: pages-endpoint shape was /chapters/{id}/pages
            // -> /chapters/{id} per 17.2-PAGES-ENDPOINT-SURVEY.md (winner verdict
            // 2026-05-10 live survey: bundle's signer allowlist rejects all
            // /chapters/{id}/<suffix> shapes; the bare /chapters/{id} endpoint is
            // signer-accepted AND server-200, with the chapter-detail body now
            // embedding `pages: { baseUrl, items[{width,height,url}] }` directly).
            //
            // Regression guard: assert ComixParser.cs's chapter DownloadUrl
            // construction targets the survey-winner shape (/api/v1/chapters/{id}
            // — no /pages suffix) AND carries the Phase 17.2 GAP-17-E annotation
            // marker so future maintainers don't accidentally revert to the
            // /pages suffix (which the signer's input-shape allowlist now
            // rejects).
            //
            // The guard reads ComixParser.cs source via the `_parserSource`
            // field (parameterized walk-up pattern), NOT ComixPuppeteerSigner.cs
            // — the apiPath construction lives upstream of the signer call.
            _parserSource.Should().Contain("Phase 17.2 GAP-17-E",
                "Phase 17.2 GAP-17-E annotation block must remain alongside the chapter " +
                "DownloadUrl construction in ComixParser.cs so the survey-winner shape " +
                "rationale isn't lost in future refactors. See 17.2-PAGES-ENDPOINT-SURVEY.md.");

            // The DownloadUrl construction must NOT include the legacy /pages suffix.
            // If the literal `/chapters/{ch.Id}/pages` reappears, the signer rejects
            // the path-shape and the live ProxyFetchPages fixture goes red.
            _parserSource.Should().NotMatchRegex(
                @"DownloadUrl\s*=\s*\$""[^""]*?/api/v1/chapters/\{ch\.Id\}/pages""",
                "Pages-endpoint DownloadUrl MUST NOT carry the legacy /pages suffix per " +
                "17.2-PAGES-ENDPOINT-SURVEY.md — the bundle's signer allowlist rejects " +
                "all /chapters/{id}/<suffix> shapes since 2026-05-10. Use " +
                "/api/v1/chapters/{ch.Id} (no /pages) — the chapter-detail endpoint " +
                "embeds the pages list directly under `pages.{baseUrl, items}`.");

            _parserSource.Should().MatchRegex(
                @"DownloadUrl\s*=\s*\$""[^""]*?/api/v1/chapters/\{ch\.Id\}""",
                "Pages-endpoint DownloadUrl construction must reference the survey-winner " +
                "shape `/api/v1/chapters/{ch.Id}` (no /pages suffix) — see " +
                "17.2-PAGES-ENDPOINT-SURVEY.md § Winner verdict.");
        }
    }
}
