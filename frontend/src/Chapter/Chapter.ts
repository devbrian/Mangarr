// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Episode/Episode.ts.
//
// Manga sibling diverges from Episode:
//   * No tvdbId, seasonNumber, episodeNumber, scene*, runtime, finaleType, airDate*.
//   * Adds chapterNumber (decimal projected to JS number),
//     absoluteChapterNumber, volumeNumber (display-only — no Volumes table per
//     PROJECT.md Out-of-Scope), translatedLanguage (BCP-47), scanlationGroup,
//     isSynthetic, chapterType, releaseDate.
//
// Backend shape source: src/Sonarr.Api.V5/Manga/Chapter/ChapterResource.cs
// (Phase 7 Plan 07-01). Field set is exact; do NOT add fields the backend does
// not emit (e.g. lastSearchTime, grabDate are NOT in ChapterResource v1).
//
// Phase 8 cleanup: collapse with Episode when Tv/ deletes.
import ModelBase from 'App/ModelBase';
import Manga from 'Manga/Manga';

export type ChapterType = 'Regular' | 'Special' | 'Oneshot' | 'Extra';

interface Chapter extends ModelBase {
  mangaId: number;
  chapterFileId?: number;
  chapterNumber: number;             // decimal projected to JS number
  absoluteChapterNumber?: number;
  volumeNumber?: number;             // display-only — no Volumes table
  title?: string;
  translatedLanguage?: string;       // BCP-47 (e.g. 'en', 'es', 'ja'); 'und' for synthetic
  scanlationGroup?: string;
  chapterType?: ChapterType;
  isSynthetic: boolean;
  releaseDate?: string;
  monitored: boolean;
  hasFile: boolean;
  externalId?: string;
  manga?: Manga;
}

export default Chapter;
