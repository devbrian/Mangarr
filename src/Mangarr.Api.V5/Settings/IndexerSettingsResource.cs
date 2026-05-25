using Mangarr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Mangarr.Api.V5.Settings
{
    public class IndexerSettingsResource : RestResource
    {
        public int MinimumAge { get; set; }
        public int Retention { get; set; }
        public int MaximumSize { get; set; }
        public int RssSyncInterval { get; set; }

        // Phase 33.2 D-04/D-05: the ONE app-wide Cloudflare solver endpoint URL. Persisted via
        // IConfigService.CloudflareSolverUrl. Write-back is automatic — SettingsController<T>.SaveSettings
        // reflects over the resource's properties and routes each into IConfigService.SaveConfigDictionary,
        // which matches this property name to the CloudflareSolverUrl config key. Empty = unconfigured.
        public string? CloudflareSolverUrl { get; set; }
    }

    public static class IndexerConfigResourceMapper
    {
        public static IndexerSettingsResource ToResource(IConfigService model)
        {
            return new IndexerSettingsResource
            {
                MinimumAge = model.MinimumAge,
                Retention = model.Retention,
                MaximumSize = model.MaximumSize,
                RssSyncInterval = model.RssSyncInterval,
                CloudflareSolverUrl = model.CloudflareSolverUrl
            };
        }
    }
}
