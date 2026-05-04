// Sonarr divergence: NEW manga sibling per Phase 7 D-07 — see DIVERGENCE.md.
// Role-match analog: frontend/src/typings/Queue.ts.
//
// Manga sibling preserves: ModelBase + URL-shaped React Query key
// (`['/manga/queue']` per Plan 07-02 contract).
//
// Manga sibling diverges from Queue:
//   * No quality / languages enum (manga has no quality model per Phase 5 D-04;
//     translatedLanguage is BCP-47 string per Phase 3 D-Q4).
//   * Adds scanlationGroup as a first-class field.
//   * Adds chapterIds array (the set of chapters covered by this row).
//   * Drops episodesWithFilesCount, seasonNumbers, isFullSeason.
//
// Backend shape source: src/Sonarr.Api.V5/Manga/Queue/MangaQueueResource.cs
// (Phase 6 Plan 06-09). Field set is exact-mirror at the wire layer.
//
// Phase 8 cleanup: collapse with Queue when Tv/ deletes.
import ModelBase from 'App/ModelBase';
import DownloadProtocol from 'DownloadClient/DownloadProtocol';

export interface MangaQueueSubresource {
  id: number;
  title?: string;
}

export interface ChapterQueueSubresource {
  id: number;
  mangaId: number;
  chapterNumber: number;
  title?: string;
  translatedLanguage?: string;
}

export interface QueueStatusMessage {
  title: string;
  messages: string[];
}

export interface MangaQueueItem extends ModelBase {
  mangaId?: number;
  chapterId?: number;
  chapterIds?: number[];
  translatedLanguage?: string;
  scanlationGroup?: string;
  size: number;
  title?: string;
  sizeLeft: number;
  timeLeft?: string;
  estimatedCompletionTime?: string;
  added?: string;
  status?: string;
  trackedDownloadStatus?: string;
  trackedDownloadState?: string;
  statusMessages?: QueueStatusMessage[];
  errorMessage?: string;
  downloadId?: string;
  indexer?: string;
  outputPath?: string;
  protocol: DownloadProtocol;
  downloadClient?: string;
  downloadClientHasPostImportCategory: boolean;
  manga?: MangaQueueSubresource;
  chapter?: ChapterQueueSubresource;
}

export default MangaQueueItem;
