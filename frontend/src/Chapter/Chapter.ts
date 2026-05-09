// Sonarr divergence: REWRITTEN per Phase 16 STRUCT-09 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Episode/Episode.ts.
//
// Pre-Phase-16 Chapter typing carried 4 per-language fields (translatedLanguage,
// scanlationGroup, isSynthetic, releaseDate) directly because the underlying API
// resource was per-(MangaId, ChapterNumber, TranslatedLanguage, ScanlationGroup).
// Post-Phase-16 the canonical Chapter row is language-free; per-language data lives
// on the new releases[] nested collection. firstReleaseDate is the upstream chapter-publish
// date (Sonarr-mirror of episode.airDateUtc); releases[].releaseDate is per-translation
// upload time (distinct semantic).
//
// Manga sibling diverges from Episode:
//   * No tvdbId, seasonNumber, episodeNumber, scene*, runtime, finaleType, airDate*.
//   * Adds chapterNumber (decimal projected to JS number),
//     absoluteChapterNumber, volumeNumber (display-only — no Volumes table per
//     PROJECT.md Out-of-Scope), chapterType, firstReleaseDate (D-02), and a nested
//     releases[] per-translation collection (STRUCT-08).
//
// Backend shape source: src/Mangarr.Api.V5/Manga/Chapter/ChapterResource.cs
// (Phase 16 Plan 16-05). Field set is exact; do NOT add fields the backend does
// not emit (e.g. lastSearchTime, grabDate are NOT in ChapterResource v1).
//
// Phase 8 cleanup: collapse with Episode when Tv/ deletes.
import ModelBase from 'App/ModelBase';
import { ChapterRelease } from 'Chapter/ChapterRelease';
import Manga from 'Manga/Manga';

export type ChapterType = 'Regular' | 'Special' | 'Oneshot' | 'Extra';

interface Chapter extends ModelBase {
  mangaId: number;
  chapterFileId?: number;
  chapterNumber: number;             // decimal projected to JS number
  absoluteChapterNumber?: number;
  volumeNumber?: number;             // display-only — no Volumes table
  title?: string;
  chapterType?: ChapterType;
  firstReleaseDate?: string;         // Phase 16 D-02 — ISO 8601; Sonarr-mirror of episode.airDateUtc
  monitored: boolean;
  hasFile: boolean;
  externalId?: string;
  releases: ChapterRelease[];        // Phase 16 STRUCT-08 — per-translation collection
  manga?: Manga;
}

export default Chapter;
