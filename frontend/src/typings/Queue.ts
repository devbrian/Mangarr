import ModelBase from 'App/ModelBase';
import DownloadProtocol from 'DownloadClient/DownloadProtocol';
import Language from 'Language/Language';
import CustomFormat from 'typings/CustomFormat';

// Sonarr divergence: Phase 15 Plan 15-12 — Episode/Episode + Quality/Quality
// imports stripped per cascade absorption (Plan 15-07 deleted Episode subtree;
// Plan 15-03 deleted Quality cascade). `episodes` typed as `unknown[]` and
// `quality` typed as `unknown` so legacy TV-shape Queue rows compile; manga
// uses typings/MangaQueueItem.ts instead. Phase 8 cleanup: collapse with MangaQueueItem.

export type QueueTrackedDownloadStatus = 'ok' | 'warning' | 'error';

export type QueueTrackedDownloadState =
  | 'downloading'
  | 'importBlocked'
  | 'importPending'
  | 'importing'
  | 'imported'
  | 'failedPending'
  | 'failed'
  | 'ignored';

export interface StatusMessage {
  title: string;
  messages: string[];
}

interface Queue extends ModelBase {
  languages: Language[];
  quality: unknown;
  customFormats: CustomFormat[];
  customFormatScore: number;
  size: number;
  title: string;
  sizeLeft: number;
  // Phase 36 Plan 06 (D-01 / LOOP-05): ADDITIVE manga-native page-progress caption fields
  // mirroring MangaQueueResource. Absent on the gateway path (Phase 38) → bytes/% fallback.
  totalPages?: number;
  completedPages?: number;
  timeLeft: string;
  estimatedCompletionTime: string;
  added?: string;
  status: string;
  trackedDownloadStatus: QueueTrackedDownloadStatus;
  trackedDownloadState: QueueTrackedDownloadState;
  statusMessages: StatusMessage[];
  errorMessage: string;
  downloadId: string;
  protocol: DownloadProtocol;
  downloadClient: string;
  outputPath: string;
  episodesWithFilesCount: number;
  seriesId?: number;
  episodeIds: number[];
  seasonNumbers: number[];
  downloadClientHasPostImportCategory: boolean;
  isFullSeason: boolean;
  episodes?: unknown[];
}

export default Queue;
