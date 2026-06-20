using Mangarr.Http.REST;
using NzbDrone.Core.ImportLists.Exclusions;

namespace Mangarr.Api.V5.ImportLists;

// Phase 26 Plan 26-05 (IL-05) — ported from
// .planning/reference/sonarr-vertical-slices/import-lists/v5-controller/ImportListExclusionResource.cs
// with the Sonarr single-int identifier field replaced by Mangarr's manga-ID
// quad (MangaDexId / MalId / AniListId / MangaBakaId) per Migration 003 + Migration 017
// + Plan 26-04's ImportListExclusion entity shape.
//
// MangaDexId is the canonical UNIQUE-indexed string identifier (see Migration 003);
// MalId + AniListId + MangaBakaId are optional secondary identifiers that ride alongside.
// MangaBakaId (quick-260619-spc, Migration 017) is the v1.3 default-primary anchor —
// un-validated, mirroring the un-indexed MalId/AniListId secondary members.
public class ImportListExclusionResource : RestResource
{
    public string? MangaDexId { get; set; }
    public int? MalId { get; set; }
    public int? AniListId { get; set; }
    public int? MangaBakaId { get; set; }
    public string? Title { get; set; }
}

public static class ImportListExclusionResourceMapper
{
    public static ImportListExclusionResource ToResource(this ImportListExclusion model)
    {
        return new ImportListExclusionResource
        {
            Id = model.Id,
            MangaDexId = model.MangaDexId,
            MalId = model.MalId,
            AniListId = model.AniListId,
            MangaBakaId = model.MangaBakaId,
            Title = model.Title
        };
    }

    public static ImportListExclusion ToModel(this ImportListExclusionResource resource)
    {
        return new ImportListExclusion
        {
            Id = resource.Id,
            MangaDexId = resource.MangaDexId,
            MalId = resource.MalId,
            AniListId = resource.AniListId,
            MangaBakaId = resource.MangaBakaId,
            Title = resource.Title
        };
    }

    public static List<ImportListExclusionResource> ToResource(this IEnumerable<ImportListExclusion> models)
    {
        return models.Select(ToResource).ToList();
    }
}
