// Sonarr divergence: NEW manga sibling per issue #84 (Plan 17.3-16 deferral
// resolution — Option A: author the peer dir to mirror frontend/src/Chapter/
// shape so the backend `src/NzbDrone.Core/MediaFiles/ChapterFile.cs` entity has
// a 1:1 frontend peer dir). See DIVERGENCE.md.
//
// Role-match analog: frontend/src/EpisodeFile/EpisodeFile.ts on the
// `v5-develop` Sonarr reference branch (shape-inspiration; the manga sibling
// trims TV-only fields per Phase 13 Plan 13-07 ChapterFileResource.cs).
//
// Manga sibling preserves: ModelBase extension + camelCase wire field set.
//
// Manga sibling diverges from EpisodeFile:
//   * Drop seasonNumber (PROJECT.md "Volumes/Seasons" Out-of-Scope).
//   * Drop quality (QualityModel), customFormats, customFormatScore,
//     qualityCutoffNotMet (Phase 5 D-05 — manga uses Custom Format +
//     Translation Profile, not QualityModel; the cutoff/CF columns belong to
//     MangaCutoffController + the Translation/CustomFormatProfile editors).
//   * Drop sceneName, indexerFlags, releaseType, mediaInfo (TV-only — no
//     scene-release naming, no torrent indexer flags, no video MediaInfo for
//     image-archive artifacts).
//   * Replace `seriesId` with `mangaId`; add `chapterId` (manga
//     ChapterFile is per-chapter, not per-(season,episode)).
//   * Replace `languages: Language[]` with `translatedLanguage?: string`
//     (BCP-47 single string per Phase 16.1 D-04 group-axis collapse).
//   * Rename `releaseGroup` to `scanlationGroup` (Phase 16.1 D-04/D-05/D-06 —
//     scanlation groups ARE the release groups for manga).
//
// Backend shape source: src/Mangarr.Api.V5/Manga/Chapter/ChapterFileResource.cs.
// Field set is exact; do NOT add fields the backend does not emit.
//
// Phase 8 cleanup: was — collapse with EpisodeFile when Tv/ deletes; Phase 17.3
// Plan 17.3-13 already retired the TV stub-dir family, so this directory is
// the canonical frontend home for chapter-file domain types/hooks/components.
import ModelBase from 'App/ModelBase';

interface ChapterFile extends ModelBase {
  mangaId: number;
  chapterId: number;
  relativePath?: string;
  path?: string;
  size: number;
  dateAdded: string;
  translatedLanguage?: string; // BCP-47; Phase 16.1 D-04
  scanlationGroup?: string; // Phase 16.1 D-04/D-05/D-06 — canonical release-group axis for manga
}

export default ChapterFile;
