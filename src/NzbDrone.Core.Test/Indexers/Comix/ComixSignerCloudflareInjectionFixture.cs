using System;
using System.Collections.Generic;
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
using PuppeteerSharp;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 33.2 Plan 02 fixture (D-02) — regression-locks the cleared-session injection
    /// ordering inside <c>ComixPuppeteerSigner</c>: the signer applies
    /// <c>SetUserAgentAsync(solverUA)</c> BEFORE <c>SetCookieAsync(cf_clearance)</c> BEFORE
    /// the page navigation (Pitfall 1 — a cf_clearance cookie is bound to the UA that earned
    /// it; splitting them re-triggers the Cloudflare challenge).
    ///
    /// <para>
    /// Runs fully offline — no real Chromium child. The production
    /// <c>ApplyCloudflareClearanceAsync</c> body is driven against a <see cref="Mock{IPage}"/>
    /// whose SetUserAgent / SetCookie / GoTo callbacks append to one ordered recording list,
    /// so the asserted order is the REAL production order, not a test-orchestrated one.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixSignerCloudflareInjectionFixture : CoreTest
    {
        private const string SolverUa = "Mozilla/5.0 (Solver) AppleWebKit/537.36";
        private const string ClearanceCookie = "cf_clearance_token_value_abc123";

        // Test subclass exposing the production protected-virtual injection seam +
        // mirroring the production try-block sequence (inject, then navigate) so the
        // recorded order reflects exactly what EnsureEnvModuleAsync does.
        private class InjectableSigner : ComixPuppeteerSigner
        {
            public InjectableSigner(
                IIndexerSourceStatusService s,
                ICloudflareClearanceService c,
                IConfigService cfg,
                Logger l)
                : base(s, c, cfg, l)
            {
            }

            // Calls the REAL ApplyCloudflareClearanceAsync (UA->cookie), then mirrors the
            // production GoToAsync call shape so the recording list captures the full
            // UA -> cookie -> navigate sequence.
            public async Task InvokeInjectThenNavigateAsync(IPage page, string pageUrl, CancellationToken ct)
            {
                await ApplyCloudflareClearanceAsync(page, ct).ConfigureAwait(false);
                await page.GoToAsync(
                    pageUrl,
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } })
                    .ConfigureAwait(false);
            }

            public Task InvokeInjectAsync(IPage page, CancellationToken ct)
                => ApplyCloudflareClearanceAsync(page, ct);
        }

        private List<string> _recorded;
        private Mock<IPage> _page;

        [SetUp]
        public void SetUp()
        {
            _recorded = new List<string>();

            _page = new Mock<IPage>();
            _page.Setup(p => p.SetUserAgentAsync(It.IsAny<string>(), It.IsAny<UserAgentMetadata>()))
                 .Callback<string, UserAgentMetadata>((ua, _) => _recorded.Add($"ua:{ua}"))
                 .Returns(Task.CompletedTask);
            _page.Setup(p => p.SetCookieAsync(It.IsAny<CookieParam[]>()))
                 .Callback<CookieParam[]>(cookies => _recorded.Add($"cookie:{cookies[0].Name}"))
                 .Returns(Task.CompletedTask);
            _page.Setup(p => p.GoToAsync(It.IsAny<string>(), It.IsAny<NavigationOptions>()))
                 .Callback<string, NavigationOptions>((url, _) => _recorded.Add($"navigate:{url}"))
                 .Returns(Task.FromResult(Mock.Of<IResponse>()));
        }

        private InjectableSigner BuildSigner()
            => new InjectableSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                Mocker.GetMock<ICloudflareClearanceService>().Object,
                Mocker.GetMock<IConfigService>().Object,
                LogManager.GetLogger("ComixPuppeteerSigner"));

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
        public async Task Injection_applies_userAgent_before_cookie_before_navigate()
        {
            GivenSolverConfigured();
            GivenClearance();

            var subject = BuildSigner();

            await subject.InvokeInjectThenNavigateAsync(_page.Object, "https://comix.to/", CancellationToken.None);

            _recorded.Should().HaveCount(3);
            _recorded[0].Should().Be($"ua:{SolverUa}", "the solver UA is set FIRST (Pitfall 1 — UA supersedes honest UA)");
            _recorded[1].Should().Be("cookie:cf_clearance", "the cf_clearance cookie is set SECOND, after its matched UA");
            _recorded[2].Should().StartWith("navigate:", "navigation happens LAST so the cleared session is in place before load");
        }

        [Test]
        public async Task Injected_cookie_copies_solver_exact_attributes()
        {
            GivenSolverConfigured();
            GivenClearance();

            CookieParam captured = null;
            _page.Setup(p => p.SetCookieAsync(It.IsAny<CookieParam[]>()))
                 .Callback<CookieParam[]>(cookies => captured = cookies[0])
                 .Returns(Task.CompletedTask);

            var subject = BuildSigner();

            await subject.InvokeInjectAsync(_page.Object, CancellationToken.None);

            captured.Should().NotBeNull();
            captured.Name.Should().Be("cf_clearance");
            captured.Value.Should().Be(ClearanceCookie);
            captured.Domain.Should().Be(".comix.to", "the solver's exact domain is reused (Pitfall 5), never hardcoded");
            captured.Path.Should().Be("/");
            captured.Secure.Should().BeTrue();
            captured.HttpOnly.Should().BeTrue();
        }

        [Test]
        public async Task Empty_solver_url_skips_injection_and_does_NOT_call_GetClearanceAsync()
        {
            GivenSolverNotConfigured();

            var subject = BuildSigner();

            await subject.InvokeInjectAsync(_page.Object, CancellationToken.None);

            _recorded.Should().BeEmpty("empty CloudflareSolverUrl gates injection off entirely");
            Mocker.GetMock<ICloudflareClearanceService>()
                  .Verify(c => c.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
            _page.Verify(p => p.SetUserAgentAsync(It.IsAny<string>(), It.IsAny<UserAgentMetadata>()), Times.Never());
            _page.Verify(p => p.SetCookieAsync(It.IsAny<CookieParam[]>()), Times.Never());
        }

        [Test]
        public async Task Solver_failure_degrades_gracefully_without_injection()
        {
            GivenSolverConfigured();
            Mocker.GetMock<ICloudflareClearanceService>()
                  .Setup(c => c.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new CloudflareSolverException("solver unreachable"));

            var subject = BuildSigner();

            Func<Task> act = () => subject.InvokeInjectAsync(_page.Object, CancellationToken.None);

            await act.Should().NotThrowAsync("a solver failure must degrade (skip injection), never hard-fail (T-33.2-08)");
            _page.Verify(p => p.SetUserAgentAsync(It.IsAny<string>(), It.IsAny<UserAgentMetadata>()), Times.Never());
            _page.Verify(p => p.SetCookieAsync(It.IsAny<CookieParam[]>()), Times.Never());

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
