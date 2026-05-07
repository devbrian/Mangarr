using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Manga.Chapter;

// Sonarr divergence: NEW manga V5 resource per Phase 13 Plan 13-06 — see DIVERGENCE.md.
// Role-match analog: src/Sonarr.Api.V5/Episodes/RenameEpisodeResource.cs (lines 5-35).
//
// Manga sibling preserves: RestResource base + ToResource extension mapper convention,
// `Id = ChapterFileId` resource-id-from-file-id mapping (mirrors TV peer's
// `Id = EpisodeFileId`).
//
// Manga sibling diverges from RenameEpisodeResource:
//   * Drop `SeasonNumber` (PROJECT.md Volumes/Seasons Out-of-Scope; manga has no
//     season concept — flat chapter list).
//   * Rename Series→Manga, Episode→Chapter.
//   * `ChapterNumbers` is `List<decimal>` (NOT `List<int>` like TV's `EpisodeNumbers`)
//     per Phase 2 D-12 widen — chapter numbers are decimal (1.5, 1.123, etc.).
//   * Source model is `RenameChapterFilePreview` (manga peer of TV's
//     `RenameEpisodeFilePreview`).
//
// Phase 15 cleanup: collapse with RenameEpisodeResource when Tv/ deletes.
public class RenameChapterResource : RestResource
{
    public int MangaId { get; set; }
    public List<int> ChapterIds { get; set; } = [];
    public List<decimal> ChapterNumbers { get; set; } = [];
    public int ChapterFileId { get; set; }
    public string? ExistingPath { get; set; }
    public string? NewPath { get; set; }
}

public static class RenameChapterResourceMapper
{
    public static RenameChapterResource ToResource(this NzbDrone.Core.MediaFiles.RenameChapterFilePreview model)
    {
        return new RenameChapterResource
        {
            Id = model.ChapterFileId,
            MangaId = model.MangaId,
            ChapterIds = model.ChapterIds.ToList(),
            ChapterNumbers = model.ChapterNumbers.ToList(),
            ChapterFileId = model.ChapterFileId,
            ExistingPath = model.ExistingPath,
            NewPath = model.NewPath
        };
    }

    public static List<RenameChapterResource> ToResource(this IEnumerable<NzbDrone.Core.MediaFiles.RenameChapterFilePreview> models)
    {
        return models.Select(ToResource).ToList();
    }
}
