using FluentValidation;
using Mangarr.Http;
using Mangarr.Http.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Cloudflare;
using NzbDrone.Core.Validation;

namespace Mangarr.Api.V5.Settings
{
    [V5ApiController("settings/indexer")]
    public class IndexerSettingsController : SettingsController<IndexerSettingsResource>
    {
        private readonly ICloudflareClearanceService _clearanceService;

        public IndexerSettingsController(IConfigFileProvider configFileProvider,
            IConfigService configService,
            ICloudflareClearanceService clearanceService)
            : base(configFileProvider, configService)
        {
            _clearanceService = clearanceService;

            SharedValidator.RuleFor(c => c.MinimumAge)
                           .GreaterThanOrEqualTo(0);

            SharedValidator.RuleFor(c => c.Retention)
                           .GreaterThanOrEqualTo(0);

            SharedValidator.RuleFor(c => c.RssSyncInterval)
                           .IsValidRssSyncInterval();

            // Phase 33.2 D-05 / T-33.2-09 (SSRF, ASVS V5/V13): the solver URL is operator config
            // that Mangarr POSTs to. Empty is allowed (unconfigured); a non-empty value MUST be a
            // valid http(s) root URL (reuses the same .ValidRootUrl() validator the indexer BaseUrl
            // uses). Non-http schemes (file://, gopher://, etc.) are rejected at the boundary.
            SharedValidator.RuleFor(c => c.CloudflareSolverUrl)
                           .ValidRootUrl()
                           .When(c => !string.IsNullOrWhiteSpace(c.CloudflareSolverUrl));
        }

        protected override IndexerSettingsResource ToResource(IConfigFileProvider configFile, IConfigService model)
        {
            return IndexerConfigResourceMapper.ToResource(model);
        }

        // Phase 33.2 D-05: Test Connection. Probe-solves the configured sidecar against a known
        // aggregator host (https://comix.to/) — the only test that proves the solver can actually
        // CLEAR, not merely answer (RESEARCH Open Question 1). Returns a typed OK/error result.
        // T-33.2-10 / ASVS V7: the response body and logs NEVER include the cf_clearance cookie
        // value or the solver's HTML — only a boolean + a non-sensitive message.
        [HttpPost("test")]
        [Produces("application/json")]
        public Ok<CloudflareSolverTestResult> TestSolver(CancellationToken cancellationToken)
        {
            try
            {
                _clearanceService.GetClearanceAsync("https://comix.to/", cancellationToken)
                                 .GetAwaiter().GetResult();

                return TypedResults.Ok(new CloudflareSolverTestResult
                {
                    IsValid = true,
                    Message = "Cloudflare solver responded successfully"
                });
            }
            catch (CloudflareSolverNotConfiguredException)
            {
                return TypedResults.Ok(new CloudflareSolverTestResult
                {
                    IsValid = false,
                    Message = "Cloudflare solver URL is not configured"
                });
            }
            catch (CloudflareSolverException ex)
            {
                // ex.Message carries host + solver status only (never the cookie/HTML — T-33.2-01).
                return TypedResults.Ok(new CloudflareSolverTestResult
                {
                    IsValid = false,
                    Message = ex.Message
                });
            }
            catch
            {
                // Connection failures, timeouts, etc. — the sidecar is unreachable. Do NOT echo the
                // raw exception (may contain the target URL/internals); use a fixed safe message.
                return TypedResults.Ok(new CloudflareSolverTestResult
                {
                    IsValid = false,
                    Message = "Cloudflare solver is unreachable"
                });
            }
        }
    }

    public class CloudflareSolverTestResult
    {
        public bool IsValid { get; set; }
        public string? Message { get; set; }
    }
}
