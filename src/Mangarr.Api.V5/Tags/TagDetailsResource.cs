using Mangarr.Http.REST;
using NzbDrone.Core.Tags;

namespace Mangarr.Api.V5.Tags;

// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — ImportListIds + AutoTagIds + SeriesIds
// stripped per Plan 15-04 ImportLists MOVE + Plan 15-10 AutoTagging DELETE + TV Series DELETE.
// MangaIds added (manga-shape replacement). Field deletions are V5 BREAKING but no manga UI ever
// referenced these stripped collections (TV-only Sonarr UI surface).
public class TagDetailsResource : RestResource
{
    public string? Label { get; set; }
    public List<int> DelayProfileIds { get; set; } = [];
    public List<int> NotificationIds { get; set; } = [];
    public List<int> RestrictionIds { get; set; } = [];
    public List<int> ExcludedReleaseProfileIds { get; set; } = [];
    public List<int> IndexerIds { get; set; } = [];
    public List<int> DownloadClientIds { get; set; } = [];
    public List<int> MangaIds { get; set; } = [];
}

public static class TagDetailsResourceMapper
{
    public static TagDetailsResource ToResource(this TagDetails model)
    {
        return new TagDetailsResource
        {
            Id = model.Id,
            Label = model.Label,
            DelayProfileIds = model.DelayProfileIds ?? [],
            NotificationIds = model.NotificationIds ?? [],
            RestrictionIds = model.RestrictionIds ?? [],
            ExcludedReleaseProfileIds = model.ExcludedReleaseProfileIds ?? [],
            IndexerIds = model.IndexerIds ?? [],
            DownloadClientIds = model.DownloadClientIds ?? [],
            MangaIds = model.MangaIds ?? []
        };
    }

    public static List<TagDetailsResource> ToResource(this IEnumerable<TagDetails> models)
    {
        return models.Select(ToResource).ToList();
    }
}
