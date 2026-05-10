using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Test.Common.Categories;

namespace Mangarr.Comix.Live.Test
{
    /// <summary>
    /// Phase 17.2 GAP-17-E pages-endpoint survey fixture (Plan 17.2-02 Task 1).
    ///
    /// <para>
    /// The orchestrator's 2026-05-10 Playwright re-verification surfaced GAP-17-E:
    /// <c>signer('/chapters/{id}/pages')</c> returns NULL on the current comix.to bundle
    /// — the bundle's path-shape allowlist rejects that path. The bundle has either
    /// renamed the endpoint or restructured how it's signed.
    /// </para>
    ///
    /// <para>
    /// Plan 17.2-02 Task 1 calls for a Playwright MCP probe of live comix.to to find
    /// the current pages-endpoint shape. The orchestrator's MCP environment supports
    /// it, but the executor agent does NOT have <c>mcp__playwright__browser_*</c> tools
    /// in scope. Instead, this fixture re-uses Phase 17 Plan 17-05's already-built
    /// <see cref="DiagnosticHarnessSigner"/> seam — same warm Chromium child, same
    /// <c>EvaluateRawAsync</c> page-evaluation seam, but driven from a fresh test
    /// fixture so results capture into the NUnit test output for survey artifact
    /// authoring.
    /// </para>
    ///
    /// <para>
    /// The survey performs three steps in one [Test] (single warm-page lifetime):
    ///   1. Capture <c>signer.toString()</c> source (first 4000 chars) so the survey
    ///      artifact can record the path-shape regex the bundle enforces.
    ///   2. Test each candidate path against the captured signer fn (returns null if
    ///      the bundle rejects the shape; non-null base64url token if it accepts).
    ///   3. For each accepted path, fetch <c>/api/v1{path}?_={token}</c> in the warm
    ///      page (credentials: include) and capture HTTP status + content-type +
    ///      body-head (first 500 chars).
    /// </para>
    ///
    /// <para>
    /// Threat model T-17.2-09: the survey artifact MUST NOT contain captured tokens
    /// (<c>_=</c>-suffixed signer tokens MUST be redacted). This fixture therefore
    /// returns the captured token's LENGTH and prefix-only (first 8 chars) — never the
    /// full token. The body-head capture truncates to 500 chars and the survey author
    /// (executor agent) is responsible for the final pre-commit grep check
    /// <c>grep -E '_=[A-Za-z0-9_-]{40,}'</c> returning 0 against any artifact file.
    /// </para>
    ///
    /// <para>
    /// [Explicit] so this fixture only runs when the executor invokes it by FQN —
    /// the survey is a one-shot data-collection step, not part of the standing live
    /// suite. [LiveComix] gates it out of CI per D-17 inherited.
    /// </para>
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("Phase 17.2 Plan 17.2-02 Task 1 survey — runs only when explicitly invoked via FQN filter.")]
    public class ComixPagesEndpointSurveyFixture : NzbDrone.Test.Common.TestBase<DiagnosticHarnessSigner>
    {
        // Known-good live target per 17-08-LIVE-VERIFICATION-EVIDENCE.md (Siren Scans, ch.20).
        private const string KnownGoodChapterId = "9002242";
        private const string KnownGoodMangaHid = "mr3m0";

        [Test]
        public async Task Survey_pages_endpoint_shape_against_live_signer()
        {
            await Subject.WarmAsync(CancellationToken.None);

            // ── Step 1: Capture signer.toString() source for path-shape regex extraction.
            var signerSourceProbe = $@"
              (() => {{
                try {{
                  const signer = {Subject.SignerExprForDiagnostic};
                  const src = signer.toString();
                  return JSON.stringify({{
                    ok: true,
                    length: src.length,
                    head: src.substring(0, 4000)
                  }});
                }} catch (err) {{
                  return JSON.stringify({{ ok: false, err: String(err) }});
                }}
              }})();";

            var signerSourceResult = await Subject.EvaluateRawDiagnostic(signerSourceProbe, CancellationToken.None);
            TestContext.Out.WriteLine("=== SIGNER_SOURCE_RESULT ===");
            TestContext.Out.WriteLine(signerSourceResult);

            // ── Step 2: Test each candidate path against the signer (path-shape allowlist).
            // Returns LENGTH and PREFIX of the captured token (first 8 chars) — full token
            // is NEVER surfaced per T-17.2-09 redaction policy.
            var candidatePaths = new[]
            {
                $"/chapters/{KnownGoodChapterId}/pages",                         // baseline (known reject)
                $"/api/v1/chapters/{KnownGoodChapterId}/images",                 // candidate A
                $"/api/v2/chapters/{KnownGoodChapterId}/pages",                  // candidate B
                $"/api/v1/manga/{KnownGoodMangaHid}/chapters/{KnownGoodChapterId}/pages", // candidate C
                $"/chapters/{KnownGoodChapterId}/manifest",                     // candidate D
                $"/chapters/{KnownGoodChapterId}/images",                       // candidate E (variant of A without /api/v1)
                $"/chapters/{KnownGoodChapterId}",                              // candidate F — canonical chapter detail (known accept)
                $"/chapters/{KnownGoodChapterId}/read",                         // candidate G — reader endpoint
                $"/chapters/{KnownGoodChapterId}/data",                         // candidate H — generic data
                $"/chapters/{KnownGoodChapterId}/imageData",                    // candidate I — keiyoushi-style camelCase
            };

            var pathsJsArray = "[" + string.Join(",", System.Array.ConvertAll(candidatePaths, p => $"'{p}'")) + "]";

            var signTestProbe = $@"
              (() => {{
                const signer = {Subject.SignerExprForDiagnostic};
                const paths = {pathsJsArray};
                const out = [];
                for (const p of paths) {{
                  let token = null, err = null;
                  try {{ token = signer(p); }} catch (e) {{ err = String(e); }}
                  out.push({{
                    path: p,
                    accepted: typeof token === 'string' && token.length >= 40,
                    tokenLength: typeof token === 'string' ? token.length : 0,
                    tokenPrefix: typeof token === 'string' ? token.substring(0, 8) + '...REDACTED' : null,
                    err: err
                  }});
                }}
                return JSON.stringify(out);
              }})();";

            var signTestResult = await Subject.EvaluateRawDiagnostic(signTestProbe, CancellationToken.None);
            TestContext.Out.WriteLine("=== SIGN_TEST_RESULT ===");
            TestContext.Out.WriteLine(signTestResult);

            // ── Step 3: For each accepted candidate, fetch in-page and capture status + body-head.
            // Must run inside the warm page so cookies/headers match production. Body-head is
            // truncated to 500 chars per T-17.2-09; full URL is REDACTED to scrub the token
            // suffix before logging.
            var fetchProbe = $@"
              (async () => {{
                const signer = {Subject.SignerExprForDiagnostic};
                const paths = {pathsJsArray};
                const out = [];
                for (const p of paths) {{
                  let token = null;
                  try {{ token = signer(p); }} catch (_e) {{}}
                  if (typeof token !== 'string' || token.length < 40) {{
                    out.push({{ path: p, fetched: false, reason: 'signer-rejected' }});
                    continue;
                  }}
                  const sep = p.indexOf('?') === -1 ? '?' : '&';
                  const url = '/api/v1' + p + sep + '_=' + encodeURIComponent(token);
                  let status = null, contentType = null, bodyHead = null, fetchErr = null;
                  try {{
                    const resp = await fetch(url, {{
                      credentials: 'include',
                      headers: {{ 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }}
                    }});
                    status = resp.status;
                    contentType = resp.headers.get('content-type');
                    const text = await resp.text();
                    bodyHead = text.substring(0, 500);
                  }} catch (err) {{
                    fetchErr = String(err);
                  }}
                  out.push({{
                    path: p,
                    fetched: true,
                    status: status,
                    contentType: contentType,
                    bodyHead: bodyHead,
                    fetchErr: fetchErr
                  }});
                }}
                return JSON.stringify(out);
              }})();";

            var fetchResult = await Subject.EvaluateRawDiagnostic(fetchProbe, CancellationToken.None);
            TestContext.Out.WriteLine("=== FETCH_RESULT ===");
            TestContext.Out.WriteLine(fetchResult);

            // ── Step 4: Decrypt-and-inspect the bare /chapters/{id} body shape so the
            // survey can determine whether the chapter detail endpoint embeds the page
            // images directly (in which case the production code calls the existing
            // /chapters/{id} endpoint and reads images from the decrypted body, NOT a
            // separate /pages endpoint). Mirrors EvaluateProxyFetchAsync's decrypt path
            // exactly. Body-head truncated to 1000 chars per T-17.2-09 (well within the
            // safe zone — decrypted bodies are JSON, no token leak).
            var decryptInspectProbe = $@"
              (async () => {{
                const captured = {{ req: null, res: null }};
                const fakeAxios = {{
                  interceptors: {{
                    request:  {{ use: function(fn) {{ captured.req = fn; }} }},
                    response: {{ use: function(fn) {{ captured.res = fn; }} }}
                  }},
                  defaults: {{ headers: {{ common: {{}} }}, transformRequest: [], transformResponse: [] }}
                }};
                const signer = {Subject.SignerExprForDiagnostic};
                const installer = {Subject.InstallerExprForDiagnostic};
                installer(fakeAxios);

                const apiPath = '/chapters/{KnownGoodChapterId}';
                const signablePath = apiPath.split('?')[0];
                const token = signer(signablePath);
                const sep = apiPath.indexOf('?') === -1 ? '?' : '&';
                const url = '/api/v1' + apiPath + sep + '_=' + encodeURIComponent(token);
                const resp = await fetch(url, {{
                  credentials: 'include',
                  headers: {{ 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }}
                }});
                const text = await resp.text();
                let raw;
                try {{ raw = JSON.parse(text); }} catch (_e) {{ raw = null; }}
                if (raw && typeof raw === 'object' && 'e' in raw && captured.res) {{
                  try {{
                    const fakeResp = {{
                      data: raw,
                      status: resp.status,
                      statusText: resp.statusText,
                      headers: Object.fromEntries([...resp.headers.entries()]),
                      config: {{ url: url, method: 'get', baseURL: '/api/v1' }},
                      request: {{}}
                    }};
                    const decoded = await captured.res(fakeResp);
                    const decodedStr = JSON.stringify(decoded && decoded.data);
                    return JSON.stringify({{
                      ok: true,
                      status: resp.status,
                      hasImages: decodedStr.indexOf('""images""') >= 0,
                      hasPages: decodedStr.indexOf('""pages""') >= 0,
                      hasUrl: decodedStr.indexOf('""url""') >= 0,
                      bodyHead: decodedStr.substring(0, 1000),
                      bodyLength: decodedStr.length
                    }});
                  }} catch (decryptErr) {{
                    return JSON.stringify({{ ok: false, decryptError: String(decryptErr) }});
                  }}
                }}
                return JSON.stringify({{ ok: false, reason: 'no-encrypted-envelope', bodyHead: text.substring(0, 500) }});
              }})();";

            string decryptResult;
            try
            {
                decryptResult = await Subject.EvaluateRawDiagnostic(decryptInspectProbe, CancellationToken.None);
            }
            catch (PuppeteerSharp.EvaluationFailedException ex)
            {
                decryptResult = $"{{ \"ok\": false, \"evaluationFailed\": \"{ex.Message.Replace("\"", "\\\"")}\" }}";
            }

            TestContext.Out.WriteLine("=== DECRYPT_INSPECT_RESULT (bare /chapters/{id}) ===");
            TestContext.Out.WriteLine(decryptResult);

            // ── Survey is data-collection only; pass unconditionally so all blocks
            // surface in the test output. The executor reads TestContext output to author
            // the survey artifact / SUMMARY winner-path entry.
            Assert.Pass("Survey complete; see SIGNER_SOURCE_RESULT / SIGN_TEST_RESULT / FETCH_RESULT / DECRYPT_INSPECT_RESULT blocks above.");
        }

        [OneTimeTearDown]
        public void TearDownLiveSigner() => Subject?.Dispose();
    }
}
