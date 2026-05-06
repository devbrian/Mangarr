namespace Sonarr.Api.V5.Manga.Wanted;

// Sonarr divergence: NEW manga V5 subresource enum per Phase-12 follow-up
// (canonical-resource-reuse, 2026-05-06) — see DIVERGENCE.md.
//
// Role-match analog: src/Sonarr.Api.V5/Wanted/CutoffSubresource.cs
// (TV peer — `{ Series, EpisodeFile, Images }`).
//
// Drives the `[FromQuery] MangaCutoffSubresource[]? includeSubresources`
// query param on `MangaCutoffController.GetCutoffUnmetChapters` (mirrors TV
// `CutoffController.GetCutoffUnmetEpisodes`'s enum-array shape). Replaces
// the original `bool includeManga = false` flag the manga sibling shipped at
// Plan 12-12 — TV's enum-array pattern is the canonical shape.
//
// Manga sibling diverges from `CutoffSubresource`:
//   * Single value `Manga` (TV has `{ Series, EpisodeFile, Images }`). The
//     ChapterFile subresource on the canonical `ChapterResource` is the
//     `ChapterFileId` scalar (not a nested resource) and chapter-level
//     images are deferred per Plan 06-09 rationale — so neither `EpisodeFile`
//     nor `Images` analogs apply here. A future plan that hydrates a richer
//     `ChapterFile` subresource or chapter-level cover images can extend
//     this enum non-breakingly.
//
// Phase 8 cleanup: collapse with `CutoffSubresource` when Tv/ deletes.
public enum MangaCutoffSubresource
{
    Manga
}
