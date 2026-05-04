// Sonarr divergence: NEW manga sibling per Phase 7 D-07 — see DIVERGENCE.md.
// Role-match analog: frontend/src/typings/History.ts.
//
// Manga sibling preserves: ModelBase + URL-shaped React Query key
// (`['/manga/history']` per Plan 07-02 contract).
//
// Manga sibling diverges from History:
//   * No quality / languages enum (manga has no quality model per Phase 5 D-04;
//     translatedLanguage is BCP-47 string per Phase 3 D-Q4).
//   * Adds scanlationGroup, sourceKey, releaseGuid (D-11 release-identity triple).
//   * eventType is a small string union projected from a backend enum.
//
// Backend shape source: src/Sonarr.Api.V5/Manga/History/ChapterHistoryResource.cs
// (Phase 6 Plan 06-09). Field set is exact-mirror at the wire layer.
//
// Phase 8 cleanup: collapse with History when Tv/ deletes.
import ModelBase from 'App/ModelBase';

export type ChapterHistoryEventType =
  | 'grabbed'
  | 'downloadFailed'
  | 'imported'
  | 'importFailed'
  | 'ignored';

export interface ChapterHistorySubresource {
  id: number;
  mangaId: number;
  chapterNumber: number;
  title?: string;
  translatedLanguage?: string;
}

export interface MangaHistorySubresource {
  id: number;
  title?: string;
}

export interface ChapterHistory extends ModelBase {
  mangaId: number;
  chapterId: number;
  sourceTitle?: string;
  date: string;
  eventType: ChapterHistoryEventType;
  data?: Record<string, string>;
  downloadId?: string;
  translatedLanguage?: string;
  scanlationGroup?: string;
  sourceKey?: string;
  releaseGuid?: string;
  successful: boolean;
  manga?: MangaHistorySubresource;
  chapter?: ChapterHistorySubresource;
}

export default ChapterHistory;
