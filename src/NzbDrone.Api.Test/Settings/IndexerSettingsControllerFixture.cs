using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Mangarr.Api.V5.Settings;
using Mangarr.Http.REST;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Cloudflare;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Settings
{
    // Phase 33.2 Plan 03 Task 4 — pins the V5 IndexerSettingsController Cloudflare-solver surface
    // (D-05 Test Connection + T-33.2-09 SSRF scheme validation + T-33.2-10 no-cookie-in-response).
    //
    // Path divergence (deviation Rule 3): the PLAN.md names src/Mangarr.Api.Test/Settings/, but the
    // actual API test project lives at src/NzbDrone.Api.Test/ (NzbDrone.* source-tree prefix is a
    // Phase 15 D-06 fork-heritage breadcrumb; assembly is Mangarr.Api.Test). Fixture placed in the
    // real project so dotnet test resolves it. First fixture under NzbDrone.Api.Test/Settings/.
    //
    // Role-match analog: src/NzbDrone.Api.Test/Config/ImportListConfigControllerFixture.cs
    // (TestBase<TController> + AutoMoq + reflection-lifted SharedValidator).
    //
    // Per-plan unit-test filter:
    //   dotnet test ... --filter "FullyQualifiedName~IndexerSettingsController"
    [TestFixture]
    public class IndexerSettingsControllerFixture : TestBase<IndexerSettingsController>
    {
        [Test]
        public async Task Test_returns_ok_valid_when_clearance_succeeds()
        {
            Mocker.GetMock<ICloudflareClearanceService>()
                .Setup(s => s.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new CloudflareClearance(
                    cfClearanceCookie: "secret-cf-clearance-value",
                    userAgent: "UA/1.0",
                    cookieDomain: "comix.to",
                    cookiePath: "/",
                    secure: true,
                    httpOnly: true,
                    expiresAt: System.DateTimeOffset.UtcNow.AddMinutes(20))));

            var result = await Subject.TestSolver(CancellationToken.None);

            result.Should().NotBeNull();
            result.Value.Should().NotBeNull();
            result.Value!.IsValid.Should().BeTrue();
        }

        [Test]
        public async Task Test_returns_not_configured_when_solver_url_missing()
        {
            Mocker.GetMock<ICloudflareClearanceService>()
                .Setup(s => s.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CloudflareSolverNotConfiguredException());

            var result = await Subject.TestSolver(CancellationToken.None);

            result.Value!.IsValid.Should().BeFalse();
            result.Value.Message.Should().Contain("not configured");
        }

        [Test]
        public async Task Test_returns_error_when_solver_throws()
        {
            Mocker.GetMock<ICloudflareClearanceService>()
                .Setup(s => s.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CloudflareSolverException("comix.to: 502 Bad Gateway"));

            var result = await Subject.TestSolver(CancellationToken.None);

            result.Value!.IsValid.Should().BeFalse();
        }

        [Test]
        public async Task Test_response_never_contains_cf_clearance_value()
        {
            // T-33.2-10 / ASVS V7: the cf_clearance cookie value must NEVER leak into the Test
            // response body. The clearance service returns a secret cookie; the response must
            // expose only a boolean + a non-sensitive message.
            const string secret = "secret-cf-clearance-value-DO-NOT-LEAK";

            Mocker.GetMock<ICloudflareClearanceService>()
                .Setup(s => s.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new CloudflareClearance(
                    cfClearanceCookie: secret,
                    userAgent: "UA/1.0",
                    cookieDomain: "comix.to",
                    cookiePath: "/",
                    secure: true,
                    httpOnly: true,
                    expiresAt: System.DateTimeOffset.UtcNow.AddMinutes(20))));

            var result = await Subject.TestSolver(CancellationToken.None);

            (result.Value!.Message ?? string.Empty).Should().NotContain(secret);
        }

        [Test]
        public void Validator_rejects_non_http_scheme()
        {
            // T-33.2-09 SSRF mitigation: a non-http(s) scheme must be rejected at the V5 boundary.
            var resource = new IndexerSettingsResource
            {
                Id = 1,
                MinimumAge = 0,
                Retention = 0,
                RssSyncInterval = 15,
                CloudflareSolverUrl = "file:///etc/passwd"
            };

            var result = ValidateViaSharedValidator(resource);

            result.IsValid.Should().BeFalse("non-http(s) solver URL is an SSRF surface and must be rejected");
        }

        [Test]
        public void Validator_accepts_empty_solver_url()
        {
            // Empty = unconfigured = allowed (the solver is opt-in).
            var resource = new IndexerSettingsResource
            {
                Id = 1,
                MinimumAge = 0,
                Retention = 0,
                RssSyncInterval = 15,
                CloudflareSolverUrl = string.Empty
            };

            var result = ValidateViaSharedValidator(resource);

            result.IsValid.Should().BeTrue("empty solver URL means unconfigured and is valid");
        }

        [Test]
        public void Validator_accepts_valid_https_solver_url()
        {
            var resource = new IndexerSettingsResource
            {
                Id = 1,
                MinimumAge = 0,
                Retention = 0,
                RssSyncInterval = 15,
                CloudflareSolverUrl = "https://flaresolverr.local:8191"
            };

            var result = ValidateViaSharedValidator(resource);

            result.IsValid.Should().BeTrue("a valid https root URL must pass");
        }

        // Helpers ----------------------------------------------------------------

        private ValidationResult ValidateViaSharedValidator(IndexerSettingsResource resource)
        {
            // SharedValidator is a `protected` property on RestController<TResource>; lift it via
            // reflection (canonical seam for FluentValidation rule tests against Mangarr controllers
            // — mirrors ImportListConfigControllerFixture).
            var prop = typeof(RestController<IndexerSettingsResource>)
                .GetProperty("SharedValidator", BindingFlags.NonPublic | BindingFlags.Instance);

            prop.Should().NotBeNull(
                "RestController<T>.SharedValidator must exist as a non-public instance property");

            var validator = (IValidator<IndexerSettingsResource>)prop!.GetValue(Subject);
            validator.Should().NotBeNull();

            return validator!.Validate(resource);
        }
    }
}
