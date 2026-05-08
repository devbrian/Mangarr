namespace Mangarr.Api.V5.Manga.Queue;

// Sonarr divergence: NEW manga V5 subresource enum per Phase 13 Plan 13-08
// (D-13-04 forward-prophylactic — MangaQueueDetailsController backfill) — see DIVERGENCE.md.
//
// Role-match analog: src/Mangarr.Api.V5/Queue/QueueSubresource.cs
// (TV peer — `{ Series, Episodes }`).
//
// Drives the `[FromQuery] MangaQueueSubresource[]? includeSubresources`
// query param on `MangaQueueDetailsController.GetQueue` (mirrors TV
// `QueueDetailsController.GetQueue`'s enum-array shape — `[FromQuery] QueueSubresource[]?`).
//
// Manga sibling diverges from `QueueSubresource`:
//   * `Series` → `Manga` (manga domain noun substitution per Phase 6 Plan 06-09 mapping).
//   * `Episodes` → `Chapters` (TV episode → manga chapter substitution).
//
// MangaQueueResource already declares both `Manga` (MangaSubresource) and `Chapter`
// (ChapterSubresource) fields populated unconditionally by `MangaQueueResourceMapper.ToResource`
// (MangaQueueResource.cs:84-96). The MangaQueueDetailsController applies the include-flags
// AFTER mapping by null-projecting the subresources when the include flag is false — the
// mirror of TV `QueueResource.ToResource(model, includeSeries, includeEpisodes)` which gates
// hydration via the same boolean pair (TV gates BEFORE mapping; manga gates AFTER mapping
// because the existing single-arg ToResource extension is shared with MangaQueueController).
//
// Phase 8/15 cleanup: collapse with `QueueSubresource` when Tv/ deletes (rename
// `Manga` → `Series` + `Chapters` → `Episodes` would be the inverse merge direction, so
// the Tv-side enum is the deletion target and this enum is the survivor).
public enum MangaQueueSubresource
{
    Manga,
    Chapters
}
