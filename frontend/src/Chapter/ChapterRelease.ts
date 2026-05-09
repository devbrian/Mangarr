// Sonarr divergence: NEW manga-domain typing per Phase 16 STRUCT-09 — see DIVERGENCE.md.
// TV's frontend has no per-release type because language lives on EpisodeFile.Languages.
// Manga's per-translation grain demands a nested collection — multilingual scanlations
// exist for manga but not for TV (Phase 16 STRUCT-02 + CONTEXT D-01).
// Mirrors the API V5 ChapterReleaseResource wire shape (Plan 16-05).
//
// Property semantics (Sonarr-divergence note):
//   * id                 — primary key of the underlying ChapterRelease row.
//   * translatedLanguage — BCP-47 language tag (e.g. 'en', 'es', 'ja').
//   * scanlationGroup    — translator group / publisher; optional.
//   * releaseDate        — ISO 8601 per-translation upload time. Distinct from
//                          Chapter.firstReleaseDate (chapter-publish date —
//                          Sonarr-mirror of Episode.airDateUtc).
//   * externalId         — upstream source identifier (e.g. MangaDex chapter UUID).
//
// Phase 16 D-04 contract: a Chapter with `releases.length === 0` renders as
// "Missing" (Sonarr-mirror of Episode + AirDateUtc + no EpisodeFile pattern).
export interface ChapterRelease {
  id: number;
  translatedLanguage: string;
  scanlationGroup?: string;
  releaseDate?: string;     // ISO 8601 — per-translation upload time
  externalId?: string;
}

export default ChapterRelease;
