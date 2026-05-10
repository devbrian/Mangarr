// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Episode/Episode.ts (TV peer, pre-Phase-15
// deletion — canonical Sonarr shape with no nested per-release collection).
//
// Manga sibling diverges from Episode:
//   * No tvdbId, seasonNumber, episodeNumber, scene*, runtime, finaleType, airDate*.
//   * Adds chapterNumber (decimal projected to JS number),
//     absoluteChapterNumber, volumeNumber (display-only — no Volumes table per
//     PROJECT.md Out-of-Scope), chapterType, and firstReleaseDate
//     (Sonarr-mirror of Episode.airDateUtc — chapter-publish date).
//
// Backend shape source: src/Mangarr.Api.V5/Manga/Chapter/ChapterResource.cs.
// Field set is exact; do NOT add fields the backend does not emit (e.g.
// lastSearchTime, grabDate are NOT in ChapterResource v1).
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
  chapterType?: ChapterType;
  firstReleaseDate?: string;         // ISO 8601 — Sonarr-mirror of Episode.airDateUtc
  monitored: boolean;
  hasFile: boolean;
  externalId?: string;
  manga?: Manga;
}

export default Chapter;
