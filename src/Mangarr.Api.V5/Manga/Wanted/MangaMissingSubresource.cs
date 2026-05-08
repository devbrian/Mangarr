namespace Mangarr.Api.V5.Manga.Wanted;

// Sonarr divergence: NEW manga V5 subresource enum per Phase-12 follow-up
// (canonical-resource-reuse, 2026-05-06) — see DIVERGENCE.md.
//
// Role-match analog: src/Mangarr.Api.V5/Wanted/MissingSubresource.cs
// (TV peer — `{ Series, Images }`).
//
// Drives the `[FromQuery] MangaMissingSubresource[]? includeSubresources`
// query param on `MangaMissingController.GetMissingChapters` (mirrors TV
// `MissingController.GetMissingEpisodes`'s enum-array shape). Replaces the
// original `bool includeManga = false` flag the manga sibling shipped at
// Plan 06-09 — TV's enum-array pattern is the canonical shape.
//
// Manga sibling diverges from `MissingSubresource`:
//   * Single value `Manga` (TV has `{ Series, Images }`). Manga has no
//     `Images` analog yet — chapter-level images are deferred per Plan 06-09
//     rationale. A future plan that adds chapter-level cover/preview images
//     can extend this enum non-breakingly.
//
// Phase 8 cleanup: collapse with `MissingSubresource` when Tv/ deletes.
public enum MangaMissingSubresource
{
    Manga
}
