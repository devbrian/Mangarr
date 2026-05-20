using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.ImportListTests
{
    // Phase 27 Plan 27-01 — unit tier for OAuthAwareImportListBase. Proves:
    //   1. Refresh template early-returns when token is still valid (no semaphore acquire).
    //   2. Refresh template invokes RefreshToken() when token is within lookahead window.
    //   3. D-05 SemaphoreSlim serializes concurrent callers on the same Definition.Id and
    //      the in-lock re-check collapses parallel-8 callers into exactly 1 live refresh
    //      (Pitfall 9 peer-flow defense).
    //   4. SemaphoreSlim registry keyed on Definition.Id isolates distinct ImportLists —
    //      two test-doubles with different IDs refresh in parallel without contention.
    //
    // No live HTTP / no cassettes — per-provider plans 27-02/03/04 cover wire protocol.
    // This fixture only exercises the base-class template + semaphore registry.
    [TestFixture]
    public class OAuthAwareImportListBaseFixture : CoreTest
    {
        private Mock<IHttpClient> _httpClient;
        private Mock<IImportListStatusService> _statusService;
        private Mock<IConfigService> _configService;
        private Mock<IMangaParsingService> _parsingService;
        private Mock<ILocalizationService> _localizationService;

        [SetUp]
        public void Setup()
        {
            _httpClient = Mocker.GetMock<IHttpClient>();
            _statusService = Mocker.GetMock<IImportListStatusService>();
            _configService = Mocker.GetMock<IConfigService>();
            _parsingService = Mocker.GetMock<IMangaParsingService>();
            _localizationService = Mocker.GetMock<ILocalizationService>();

            _statusService.Setup(s => s.GetBlockedProviders())
                          .Returns(new List<ImportListStatus>());
        }

        private TestOAuthImportList BuildSubject(int definitionId, DateTime expires)
        {
            var subject = new TestOAuthImportList(
                _httpClient.Object,
                _statusService.Object,
                _configService.Object,
                _parsingService.Object,
                _localizationService.Object,
                TestLogger);

            subject.Definition = new ImportListDefinition
            {
                Id = definitionId,
                Name = $"TestOAuth-{definitionId}",
                Settings = new TestOAuthImportListSettings { Expires = expires }
            };

            return subject;
        }

        [Test]
        public void refresh_returns_early_when_token_still_valid()
        {
            // Token expires in 1 hour — well outside the 5-minute lookahead default.
            var subject = BuildSubject(definitionId: 100, expires: DateTime.UtcNow.AddHours(1));

            subject.InvokeRefreshTokenIfNecessary();

            subject.RefreshCount.Should().Be(0,
                "tokens still inside their valid window must not trigger a live RefreshToken call (Trakt.cs:127-133 common-path early-return).");
        }

        [Test]
        public void refresh_invoked_when_token_within_lookahead()
        {
            // Token expires in 30 seconds — well inside the 5-minute lookahead.
            var subject = BuildSubject(definitionId: 101, expires: DateTime.UtcNow.AddSeconds(30));

            subject.InvokeRefreshTokenIfNecessary();

            subject.RefreshCount.Should().Be(1,
                "tokens within the lookahead window must trigger exactly one RefreshToken call (Trakt.cs:127-133 + Phase 27 D-05 SemaphoreSlim wrap).");
        }

        [Test]
        public void concurrent_refresh_invocations_serialize_via_semaphore()
        {
            // 8 parallel callers on the same Definition.Id; the test-double's
            // RefreshToken() impl bumps Settings.Expires to +1h on the first call.
            // The in-lock re-check (peer-flow defense) collapses callers 2..8 into
            // no-ops, so RefreshCount must be exactly 1 — NOT 8 — at the end.
            var subject = BuildSubject(definitionId: 200, expires: DateTime.UtcNow.AddSeconds(30));

            Parallel.For(0, 8, _ => subject.InvokeRefreshTokenIfNecessary());

            subject.RefreshCount.Should().Be(1,
                "D-05 + Pitfall 9: per-ImportList SemaphoreSlim must serialize concurrent " +
                "refreshes on the same Definition.Id, and the in-lock re-check (peer-flow " +
                "defense) must collapse callers 2..8 into no-ops so exactly one live refresh " +
                "happens — concurrent refreshes on MAL revoke the prior token (400 invalid_grant).");
        }

        [Test]
        public void semaphore_keyed_on_definition_id_isolates_lists()
        {
            // Two distinct ImportLists with different Definition.Id values — each must
            // get its own SemaphoreSlim entry in the registry and refresh in parallel
            // without blocking. We verify isolation by running both refreshes via
            // Task.WhenAll and asserting both incremented their own counter exactly once.
            var subjectA = BuildSubject(definitionId: 301, expires: DateTime.UtcNow.AddSeconds(30));
            var subjectB = BuildSubject(definitionId: 302, expires: DateTime.UtcNow.AddSeconds(30));

            Task.WaitAll(
                Task.Run(() => subjectA.InvokeRefreshTokenIfNecessary()),
                Task.Run(() => subjectB.InvokeRefreshTokenIfNecessary()));

            subjectA.RefreshCount.Should().Be(1, "ImportList A should refresh independently of ImportList B.");
            subjectB.RefreshCount.Should().Be(1, "ImportList B should refresh independently of ImportList A.");
        }

        // ===== Test-double types =====
        //
        // TestOAuthImportListSettings: minimal IOAuthImportListSettings POCO. Inherits
        // ImportListSettingsBase<T> per the OAuthAwareImportListBase generic constraint.
        // No real validator — Validate() returns an empty NzbDroneValidationResult to
        // avoid pulling in FluentValidation infrastructure for a base-class unit test.
        public class TestOAuthImportListSettings : ImportListSettingsBase<TestOAuthImportListSettings>, IOAuthImportListSettings
        {
            [FieldDefinition(0, Label = "Base URL")]
            public override string BaseUrl { get; set; }

            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
            public DateTime Expires { get; set; }
            public string AuthUser { get; set; }

            public override NzbDroneValidationResult Validate()
            {
                return new NzbDroneValidationResult();
            }
        }

        // TestOAuthImportList: concrete OAuthAwareImportListBase<T> subclass exposing a
        // counter on RefreshToken() so fixtures can assert exact invocation counts. The
        // RefreshToken() impl simulates a successful refresh by pushing Settings.Expires
        // to +1h, which exercises the in-lock re-check (peer-flow defense).
        public class TestOAuthImportList : OAuthAwareImportListBase<TestOAuthImportListSettings>
        {
            public int RefreshCount;

            public TestOAuthImportList(
                IHttpClient httpClient,
                IImportListStatusService importListStatusService,
                IConfigService configService,
                IMangaParsingService parsingService,
                ILocalizationService localizationService,
                Logger logger)
                : base(httpClient, importListStatusService, configService, parsingService, localizationService, logger)
            {
            }

            public override string Name => "TestOAuth";
            public override ImportListType ListType => ImportListType.Other;
            public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(24);

            public override IImportListRequestGenerator GetRequestGenerator()
            {
                throw new NotSupportedException("base-class unit fixture does not exercise the request pipeline");
            }

            public override IParseImportListResponse GetParser()
            {
                throw new NotSupportedException("base-class unit fixture does not exercise the parse pipeline");
            }

            protected override void RefreshToken()
            {
                // Simulate a successful refresh-grant — bump Expires past the lookahead
                // window so the in-lock re-check correctly observes the post-refresh
                // state on subsequent waiters.
                Interlocked.Increment(ref RefreshCount);
                Settings.Expires = DateTime.UtcNow.AddHours(1);
            }

            protected override void Test(List<ValidationFailure> failures)
            {
                // No remote endpoint — always succeeds.
            }

            // Shim exposing the protected RefreshTokenIfNecessary template to the
            // fixture. Planner-discretion mechanism per 27-01-PLAN.md Task 2 action
            // step; both an internal accessor and a public shim are Sonarr-canonical.
            public void InvokeRefreshTokenIfNecessary() => RefreshTokenIfNecessary();
        }
    }
}
