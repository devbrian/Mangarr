using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/IImportListSettings.cs.
    public interface IImportListSettings : IProviderConfig
    {
        string BaseUrl { get; set; }
    }
}
