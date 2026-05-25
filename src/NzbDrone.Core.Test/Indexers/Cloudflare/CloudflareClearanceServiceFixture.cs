using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Cloudflare;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Indexers.Cloudflare
{
    /// <summary>
    /// Phase 33.2 — clearance service behavior fixture (SOLVE-01). Drives a test subclass
    /// of <see cref="CloudflareClearanceService"/> that overrides the
    /// <c>PostSolveAsync</c> seam to return canned <see cref="FlareSolverrResponse"/>
    /// (counting invocations) and the <c>ClearanceTtl</c> seam where TTL-expiry is
    /// exercised — no live sidecar required. Asserts cf_clearance + UA parse, cache HIT
    /// avoids a second solve, TTL expiry forces a re-solve, and the typed-exception
    /// branches (not-configured / non-ok / missing cf_clearance).
    /// </summary>
    [TestFixture]
    public class CloudflareClearanceServiceFixture : CoreTest
    {
        private Mock<IConfigService> _configService;

        [SetUp]
        public void Setup()
        {
            _configService = new Mock<IConfigService>();
            _configService.SetupGet(c => c.CloudflareSolverUrl).Returns("http://solver.local:8191");
        }

        private static FlareSolverrResponse OkResponse(string cookie = "abc123", string ua = "Mozilla/5.0 (test-UA)")
        {
            return new FlareSolverrResponse
            {
                Status = "ok",
                Solution = new FlareSolverrSolution
                {
                    UserAgent = ua,
                    Cookies = new List<FlareSolverrCookie>
                    {
                        new FlareSolverrCookie
                        {
                            Name = "cf_clearance",
                            Value = cookie,
                            Domain = ".comix.to",
                            Path = "/",
                            Secure = true,
                            HttpOnly = true
                        }
                    }
                }
            };
        }

        /// <summary>Test subclass: canned solve + invocation counter + tunable TTL.</summary>
        private sealed class TestableClearanceService : CloudflareClearanceService
        {
            private readonly Func<FlareSolverrResponse> _cannedResponse;
            private readonly TimeSpan _ttl;

            public TestableClearanceService(IConfigService configService, Func<FlareSolverrResponse> cannedResponse, TimeSpan? ttl = null)
                : base(configService, TestLogger)
            {
                _cannedResponse = cannedResponse;
                _ttl = ttl ?? TimeSpan.FromMinutes(20);
            }

            public int PostSolveInvocations { get; private set; }

            protected override TimeSpan ClearanceTtl => _ttl;

            protected override Task<FlareSolverrResponse> PostSolveAsync(string solverUrl, string targetUrl, CancellationToken ct)
            {
                PostSolveInvocations++;
                return Task.FromResult(_cannedResponse());
            }
        }

        [Test]
        public async Task GetClearanceAsync_should_parse_cf_clearance_cookie_and_user_agent()
        {
            var subject = new TestableClearanceService(_configService.Object, () => OkResponse("cookie-value", "UA-value"));

            var clearance = await subject.GetClearanceAsync("https://comix.to/", CancellationToken.None);

            clearance.CfClearanceCookie.Should().Be("cookie-value");
            clearance.UserAgent.Should().Be("UA-value");
            clearance.CookieDomain.Should().Be(".comix.to");
            clearance.CookiePath.Should().Be("/");
            clearance.Secure.Should().BeTrue();
            clearance.HttpOnly.Should().BeTrue();
        }

        [Test]
        public async Task GetClearanceAsync_cache_HIT_should_not_re_solve_for_same_host()
        {
            var subject = new TestableClearanceService(_configService.Object, () => OkResponse());

            await subject.GetClearanceAsync("https://comix.to/", CancellationToken.None);
            await subject.GetClearanceAsync("https://comix.to/title/abc", CancellationToken.None);

            subject.PostSolveInvocations.Should().Be(1, "second call for the same host must hit the per-host cache");
        }

        [Test]
        public async Task GetClearanceAsync_TTL_expiry_should_force_re_solve()
        {
            // Near-zero TTL so the cached entry is already expired by the second call.
            var subject = new TestableClearanceService(_configService.Object, () => OkResponse(), TimeSpan.FromMilliseconds(-1));

            await subject.GetClearanceAsync("https://comix.to/", CancellationToken.None);
            await subject.GetClearanceAsync("https://comix.to/", CancellationToken.None);

            subject.PostSolveInvocations.Should().Be(2, "an expired cache entry must trigger a fresh solve");
        }

        [Test]
        public void GetClearanceAsync_should_throw_not_configured_when_solver_url_empty()
        {
            _configService.SetupGet(c => c.CloudflareSolverUrl).Returns(string.Empty);
            var subject = new TestableClearanceService(_configService.Object, () => OkResponse());

            Func<Task> act = () => subject.GetClearanceAsync("https://comix.to/", CancellationToken.None);

            act.Should().ThrowAsync<CloudflareSolverNotConfiguredException>();
            subject.PostSolveInvocations.Should().Be(0, "no POST should be attempted when the solver URL is empty");
        }

        [Test]
        public async Task GetClearanceAsync_should_throw_solver_exception_on_error_status()
        {
            var subject = new TestableClearanceService(_configService.Object, () => new FlareSolverrResponse { Status = "error", Message = "challenge failed" });

            Func<Task> act = () => subject.GetClearanceAsync("https://comix.to/", CancellationToken.None);

            await act.Should().ThrowAsync<CloudflareSolverException>();

            // The service logs a single Warn (host + status only — never the cookie/HTML).
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public async Task GetClearanceAsync_should_throw_solver_exception_when_cf_clearance_missing()
        {
            var subject = new TestableClearanceService(_configService.Object, () => new FlareSolverrResponse
            {
                Status = "ok",
                Solution = new FlareSolverrSolution
                {
                    UserAgent = "UA",
                    Cookies = new List<FlareSolverrCookie>
                    {
                        new FlareSolverrCookie { Name = "other_cookie", Value = "x" }
                    }
                }
            });

            Func<Task> act = () => subject.GetClearanceAsync("https://comix.to/", CancellationToken.None);

            await act.Should().ThrowAsync<CloudflareSolverException>();

            // The service logs a single Warn (host only — never the cookie/HTML).
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
