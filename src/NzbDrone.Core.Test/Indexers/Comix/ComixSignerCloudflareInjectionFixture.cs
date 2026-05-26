using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Cloudflare;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 33.2 Plan 02 (D-02) cleared-session injection — RE-EXPRESSED for the Phase 33.3
    /// Microsoft.Playwright .NET signer port.
    ///
    /// <para>
    /// PuppeteerSharp set the User-Agent per-PAGE, so the original fixture asserted the
    /// SetUserAgentAsync → SetCookieAsync → GoToAsync ordering against a <c>Mock&lt;IPage&gt;</c>.
    /// Playwright sets the UA at browser-CONTEXT creation (no per-page SetUserAgent), so the signer
    /// now resolves the clearance up front (<c>ResolveClearanceAsync</c>) and
    /// <c>LaunchAndProbeAsync</c> applies the matched UA via the context options and adds the
    /// cf_clearance cookie via <c>context.AddCookiesAsync</c>. The Pitfall-1 invariant (the cookie's
    /// matched UA is in place BEFORE the cookie, BEFORE navigation) therefore holds <b>by
    /// construction</b> — UA is a context-creation parameter, the cookie is added after the context
    /// exists, and navigation happens later still. There is nothing per-page left to order.
    /// </para>
    ///
    /// <para>
    /// This fixture locks the parts that still carry decision logic, fully offline (no Chromium):
    /// <list type="bullet">
    ///   <item><description><c>ResolveClearanceAsync</c> surfaces the solver's clearance (matched
    ///   UA + cf_clearance cookie) when the global solver URL is configured.</description></item>
    ///   <item><description>An empty solver URL gates resolution off entirely (returns null, never
    ///   calls the clearance service).</description></item>
    ///   <item><description>A solver failure degrades gracefully (returns null, logs one Warn with
    ///   host + typed-error class only — never the cookie).</description></item>
    ///   <item><description><c>BuildClearanceCookie</c> maps the solver's EXACT
    ///   domain/path/secure/httpOnly (Pitfall 5).</description></item>
    /// </list>
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixSignerCloudflareInjectionFixture : CoreTest
    {
        private const string SolverUa = "Mozilla/5.0 (Solver) AppleWebKit/537.36";
        private const string ClearanceCookie = "cf_clearance_token_value_abc123";

        // Test subclass exposing the production protected-virtual clearance-resolution seam so the
        // REAL ResolveClearanceAsync body (config-gate + GetClearanceAsync + graceful-degrade) is
        // exercised without a real Chromium child.
        private class InjectableSigner : ComixPlaywrightSigner
        {
            public InjectableSigner(
                IIndexerSourceStatusService s,
                ICloudflareClearanceService c,
                IConfigService cfg,
                Logger l)
                : base(s, c, cfg, l)
            {
            }

            public Task<CloudflareClearance> InvokeResolveClearanceAsync(CancellationToken ct)
                => ResolveClearanceAsync(ct);
        }

        [SetUp]
        public void SetUp()
        {
        }

        private InjectableSigner BuildSigner()
            => new InjectableSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                Mocker.GetMock<ICloudflareClearanceService>().Object,
                Mocker.GetMock<IConfigService>().Object,
                LogManager.GetLogger("ComixPlaywrightSigner"));

        private void GivenSolverConfigured()
            => Mocker.GetMock<IConfigService>()
                     .SetupGet(c => c.CloudflareSolverUrl)
                     .Returns("http://flaresolverr:8191/v1");

        private void GivenSolverNotConfigured()
            => Mocker.GetMock<IConfigService>()
                     .SetupGet(c => c.CloudflareSolverUrl)
                     .Returns(string.Empty);

        private void GivenClearance()
            => Mocker.GetMock<ICloudflareClearanceService>()
                     .Setup(c => c.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new CloudflareClearance(
                         cfClearanceCookie: ClearanceCookie,
                         userAgent: SolverUa,
                         cookieDomain: ".comix.to",
                         cookiePath: "/",
                         secure: true,
                         httpOnly: true,
                         expiresAt: DateTimeOffset.UtcNow.AddMinutes(20)));

        [Test]
        public async Task ResolveClearance_surfaces_matched_UA_and_cookie_when_solver_configured()
        {
            // The matched UA goes onto the browser context (so it is necessarily in place before the
            // cookie, before navigation — Pitfall 1 by construction); the cookie is added to the
            // context afterward. The fixture verifies the clearance the signer will apply carries both.
            GivenSolverConfigured();
            GivenClearance();

            var subject = BuildSigner();

            var clearance = await subject.InvokeResolveClearanceAsync(CancellationToken.None);

            clearance.Should().NotBeNull();
            clearance.UserAgent.Should().Be(SolverUa, "the solver UA supersedes the browser's own UA (Pitfall 1)");
            clearance.CfClearanceCookie.Should().Be(ClearanceCookie);
        }

        [Test]
        public void BuildClearanceCookie_copies_solver_exact_attributes()
        {
            var clearance = new CloudflareClearance(
                cfClearanceCookie: ClearanceCookie,
                userAgent: SolverUa,
                cookieDomain: ".comix.to",
                cookiePath: "/",
                secure: true,
                httpOnly: true,
                expiresAt: DateTimeOffset.UtcNow.AddMinutes(20));

            var cookie = ComixPlaywrightSigner.BuildClearanceCookie(clearance);

            cookie.Name.Should().Be("cf_clearance");
            cookie.Value.Should().Be(ClearanceCookie);
            cookie.Domain.Should().Be(".comix.to", "the solver's exact domain is reused (Pitfall 5), never hardcoded");
            cookie.Path.Should().Be("/");
            cookie.Secure.Should().BeTrue();
            cookie.HttpOnly.Should().BeTrue();
        }

        [Test]
        public async Task Empty_solver_url_returns_null_and_does_NOT_call_GetClearanceAsync()
        {
            GivenSolverNotConfigured();

            var subject = BuildSigner();

            var clearance = await subject.InvokeResolveClearanceAsync(CancellationToken.None);

            clearance.Should().BeNull("empty CloudflareSolverUrl gates clearance resolution off entirely");
            Mocker.GetMock<ICloudflareClearanceService>()
                  .Verify(c => c.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public async Task Solver_failure_degrades_gracefully_returning_null()
        {
            GivenSolverConfigured();
            Mocker.GetMock<ICloudflareClearanceService>()
                  .Setup(c => c.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new CloudflareSolverException("solver unreachable"));

            var subject = BuildSigner();

            CloudflareClearance clearance = null;
            Func<Task> act = async () => clearance = await subject.InvokeResolveClearanceAsync(CancellationToken.None);

            await act.Should().NotThrowAsync("a solver failure must degrade (return null), never hard-fail (T-33.2-08)");
            clearance.Should().BeNull();

            // The degrade path logs one Warn (host + typed-error class only — NEVER the cookie,
            // T-33.2-06 / ASVS V7). CoreTest otherwise fails the run on unexpected Warn logs.
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void ComixIndexerSettings_UseCloudflareSolver_defaults_ON()
        {
            // D-04: the per-indexer "Use Cloudflare Solver" checkbox defaults ON.
            new ComixIndexerSettings().UseCloudflareSolver.Should().BeTrue(
                "Comix is Cloudflare-protected so the solver toggle defaults ON (D-04)");
        }
    }
}
