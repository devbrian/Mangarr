using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Chromium-free upstream-drift falsifier. Locks the structural shape of
    /// <c>ComixPlaywrightSigner.cs</c> against keiyoushi <c>Comix.kt</c>'s
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
    /// record. The <c>Resources/upstream-signer.txt</c> Phase 17 port-time snapshot
    /// is retained in the repository as a historical reference documenting the
    /// obsolete namespace-probe shape, but is no longer loaded by this fixture; the
    /// captureToken-shape tests now use grep-against-implementation-source checks
    /// (<c>_signerSource</c> / <c>_parserSource</c>) — see PR #244 Comment #5 for the
    /// clarification reason.
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
                "src/NzbDrone.Core/Indexers/Comix/ComixPlaywrightSigner.cs");

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
        public void Signer_must_use_manga_bundle_oracle_via_dynamic_import_and_structural_export_probe()
        {
            // 2026-05-23 oracle pivot + Phase 33.3 structural hardening: the signer does NOT
            // captureToken+relay via vanilla HTTP. comix.to's response-encryption oracle is
            // closure-scoped inside the secure-* bundle and is only invokable via the manga-*
            // bundle's exported axios path client (which already has the bundle's `Hi(ai)`
            // decryption interceptor installed). The signer dynamic-imports that bundle from page
            // context and calls the discovered client's `.get(apiPath)` to get plaintext JSON.
            // Phase 33.3 made BOTH halves STRUCTURAL (GH #266): the bundle is matched on the stable
            // `/dist/manga-` prefix (not a per-build token) and the path-client export is DISCOVERED
            // at runtime (not pinned to a re-mangling letter like the old `mod.f`/`mod.g`). If this
            // fails, someone reverted to the upstream-obsolete captureToken+relay shape OR re-pinned
            // a rotating token/letter — read .planning/phases/33.3-…/33.3-RESEARCH.md.
            _signerSource.Should().MatchRegex(
                @"url\.IndexOf\(\s*""/dist/manga-""\s*,\s*StringComparison\.OrdinalIgnoreCase\s*\)\s*>=\s*0",
                "Signer must locate the oracle bundle via the executable structural filter " +
                "url.IndexOf(\"/dist/manga-\") (the stable manga-* chunk prefix), NOT a per-build token.");

            _signerSource.Should().Contain(
                "EnsureEnvModuleAsync",
                "Signer must call EnsureEnvModuleAsync to sniff the oracle bundle URL from " +
                "page network traffic + cache it across calls. The bundle URL is content-hashed " +
                "so it's stable per build but rotates per deploy.");

            _signerSource.Should().Contain(
                "await import(",
                "Signer must dynamic-import the manga-* bundle from page context. The bundle's " +
                "decryption oracle (Hi(ai) axios interceptor) lives in module scope and is " +
                "only reachable via ES module export.");

            _signerSource.Should().Contain(
                "window.__mangarrOracleKey",
                "Signer must DISCOVER the decrypting path-client export at runtime (probing the " +
                "manga-* bundle's exports for the `.get` client that returns a top-level `items` " +
                "array) and cache the key on `window.__mangarrOracleKey`. This structural discovery " +
                "replaces the old `mod.f`/`mod.g` pinned-letter accessor that re-broke every deploy " +
                "(export letters re-mangle per build — GH #266).");

            _signerSource.Should().NotMatchRegex(
                @"const\s+f\s*=\s*mod\.[a-z]\s*;",
                "Phase 33.3 removed the pinned single-letter accessor (`const f = mod.g;`). " +
                "Re-introducing a `mod.<letter>` pin re-creates the per-deploy rotation treadmill — " +
                "discover the export by probing for the `.get` client returning `items` instead.");

            _signerSource.Should().MatchRegex(
                @"await\s+client\.get\(",
                "Signer must invoke the DISCOVERED client (`await client.get(apiPath, opts)`) — " +
                "the executable call that returns plaintext JSON via the bundle's own interceptor chain.");
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
        public void Signer_must_split_query_into_axios_params_object()
        {
            // The env-module oracle invokes axios via `f.get(pathPart, {params:obj})`
            // (NOT `f.get(pathWithQuery)`). The signer must therefore split the
            // caller's apiPath (e.g., `/manga/mr3m0/chapters?page=1&limit=20`) into
            // path + params components.
            //
            // 2026-05-23 oracle pivot: this REPLACES the vanilla-HTTP relay path
            // (which is structurally unviable post-rotation because comix.to encrypts
            // response bodies regardless of which HTTP client issues the request — the
            // decryption only happens through the bundle's own axios instance).
            _signerSource.Should().Contain(
                "SplitApiPathToAxiosCall",
                "Signer must call SplitApiPathToAxiosCall to convert apiPath query " +
                "parameters into a JSON object suitable for axios `{params: obj}`. " +
                "See Investigation Phase 3 in .planning/debug/comix-signer-rotation.md.");

            _signerSource.Should().Contain(
                "/api/v1",
                "Even though the env module's axios baseURL handles /api/v1, the route " +
                "resolution still references the API prefix when matching outgoing " +
                "requests in EnsureEnvModuleAsync.");
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
            // Phase 33.3 (Playwright port): the navigation option is Playwright's
            // PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded } (was PuppeteerSharp's
            // NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } }).
            _signerSource.Should().Contain(
                "WaitUntilState.DOMContentLoaded",
                "captureToken navigation must use DOMContentLoaded — the bundle's long-lived " +
                "sockets defeat NetworkIdle. We wait on the captured token (the page's own " +
                "outgoing API request) rather than network quiescence.");

            // Detect actual code usage rather than mere mentions in comments/xmldoc.
            _signerSource.Should().NotMatchRegex(
                @"WaitUntilState\.NetworkIdle",
                "NetworkIdle is incompatible with the captureToken shape (the bundle never " +
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
            // field (parameterized walk-up pattern), NOT ComixPlaywrightSigner.cs
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
