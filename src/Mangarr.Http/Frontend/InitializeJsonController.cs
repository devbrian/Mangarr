using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Analytics;
using NzbDrone.Core.Configuration;

namespace Mangarr.Http.Frontend
{
    [Authorize(Policy = "UI")]
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class InitializeJsonController : Controller
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IAnalyticsService _analyticsService;

        private static string _apiKey;
        private static string _urlBase;
        private string _generatedContent;

        public InitializeJsonController(IConfigFileProvider configFileProvider,
                                      IAnalyticsService analyticsService)
        {
            _configFileProvider = configFileProvider;
            _analyticsService = analyticsService;

            _apiKey = configFileProvider.ApiKey;
            _urlBase = configFileProvider.UrlBase;
        }

        [HttpGet("/initialize.json")]
        public IActionResult Index()
        {
            return Content(GetContent(), "application/json");
        }

        private string GetContent()
        {
            if (RuntimeInfo.IsProduction && _generatedContent != null)
            {
                return _generatedContent;
            }

            var builder = new StringBuilder();
            builder.AppendLine("{");

            // Sonarr divergence: Phase 15 Plan 15-12 fix-forward — apiRoot flipped /api/v3 → /api/v5
            // because Sonarr.Api.V3 was deleted in Plan 15-06 (D-12). Frontend jQuery action
            // thunks (customFilterActions, qualityProfiles.js, importLists.js) construct URLs as
            // apiRoot + path; with apiRoot="/api/v3" they all hit deleted endpoints. The newer
            // TanStack hooks already hardcode /api/v5 in fetchJson.ts.
            builder.AppendLine($"  \"apiRoot\": \"{_urlBase}/api/v5\",");
            builder.AppendLine($"  \"apiKey\": \"{_apiKey}\",");
            builder.AppendLine($"  \"release\": \"{BuildInfo.Release}\",");
            builder.AppendLine($"  \"version\": \"{BuildInfo.Version.ToString()}\",");
            builder.AppendLine($"  \"instanceName\": \"{_configFileProvider.InstanceName.ToString()}\",");
            builder.AppendLine($"  \"theme\": \"{_configFileProvider.Theme.ToString()}\",");
            builder.AppendLine($"  \"branch\": \"{_configFileProvider.Branch.ToLower()}\",");
            builder.AppendLine($"  \"analytics\": {_analyticsService.IsEnabled.ToString().ToLowerInvariant()},");
            builder.AppendLine($"  \"userHash\": \"{HashUtil.AnonymousToken()}\",");
            builder.AppendLine($"  \"urlBase\": \"{_urlBase}\",");
            builder.AppendLine($"  \"isProduction\": {RuntimeInfo.IsProduction.ToString().ToLowerInvariant()}");
            builder.AppendLine("}");

            _generatedContent = builder.ToString();

            return _generatedContent;
        }
    }
}
