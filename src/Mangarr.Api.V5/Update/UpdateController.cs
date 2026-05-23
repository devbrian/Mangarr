using Mangarr.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.History;

namespace Mangarr.Api.V5.Update
{
    [V5ApiController]
    public class UpdateController : Controller
    {
        private readonly IRecentUpdateProvider _recentUpdateProvider;
        private readonly IUpdateHistoryService _updateHistoryService;
        private readonly IConfigFileProvider _configFileProvider;

        public UpdateController(IRecentUpdateProvider recentUpdateProvider, IUpdateHistoryService updateHistoryService, IConfigFileProvider configFileProvider)
        {
            _recentUpdateProvider = recentUpdateProvider;
            _updateHistoryService = updateHistoryService;
            _configFileProvider = configFileProvider;
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<List<UpdateResource>> GetRecentUpdates()
        {
            var resources = _recentUpdateProvider.GetRecentUpdatePackages()
                                                 .OrderByDescending(u => u.Version)
                                                 .ToResource();

            if (resources.Any())
            {
                var first = resources.First();
                first.Latest = true;

                // Phase 29 D-04 (PR #248 review — Codex P1 #1 + #2): AND in the
                // provider-declared Installable flag before exposing the resource as
                // installable. Legacy providers default to model.Installable=true so
                // the version-gate is the only effective check (behavior preserved);
                // GitHubReleasesUpdatePackageProvider sets Installable=false to keep
                // the broker banner-only (no built-in install path, so a missing
                // Hash + asset-runtime-mismatch can't reach IVerifyUpdates.Verify).
                first.Installable = first.Installable && first.Version > BuildInfo.Version;

                var installed = resources.SingleOrDefault(r => r.Version == BuildInfo.Version);

                if (installed != null)
                {
                    installed.Installed = true;
                }

                if (!_configFileProvider.LogDbEnabled)
                {
                    return TypedResults.Ok(resources);
                }

                var updateHistory = _updateHistoryService.InstalledSince(resources.Last().ReleaseDate);
                var installDates = updateHistory
                                                        .DistinctBy(v => v.Version)
                                                        .ToDictionary(v => v.Version);

                foreach (var resource in resources)
                {
                    if (installDates.TryGetValue(resource.Version, out var installDate))
                    {
                        resource.InstalledOn = installDate.Date;
                    }
                }
            }

            return TypedResults.Ok(resources);
        }
    }
}
