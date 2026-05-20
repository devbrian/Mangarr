using Mangarr.Http.REST;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;

namespace Mangarr.Api.V5.Config;

// Phase 27.1 Plan 27.1-01 — V5 KV config DTO for ListSyncLevel + ListSyncTag.
// Mirrors DownloadClientConfigResource.cs shape exactly (Mangarr-canonical
// RestResource + Mapper pattern). Field set intentionally minimal: the
// reflection-based SaveConfigDictionary sweep in ImportListConfigController
// uses BindingFlags.Instance | BindingFlags.Public — adding fields here
// directly widens the mass-assignment surface (T-27.1-01-01 mitigation).
public class ImportListConfigResource : RestResource
{
    public ListSyncLevelType ListSyncLevel { get; set; }
    public int ListSyncTag { get; set; }
}

public static class ImportListConfigResourceMapper
{
    public static ImportListConfigResource ToResource(IConfigService model)
    {
        return new ImportListConfigResource
        {
            ListSyncLevel = model.ListSyncLevel,
            ListSyncTag = model.ListSyncTag
        };
    }
}
