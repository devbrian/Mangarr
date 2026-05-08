import Language from 'Language/Language';
import CustomFormat from './CustomFormat';

export type HistoryEventType =
  | 'grabbed'
  | 'seriesFolderImported'
  | 'downloadFolderImported'
  | 'downloadFailed'
  | 'episodeFileDeleted'
  | 'episodeFileRenamed'
  | 'downloadIgnored'
  // Manga peers (ChapterHistoryEventType — Phase 6). The TV-only `seriesFolderImported`
  // / `downloadFolderImported` split has no manga analog; manga only emits a single
  // `imported` event after CompletedDownloadHandling moves the file.
  | 'imported'
  | 'importFailed';

export interface GrabbedHistoryData {
  indexer: string;
  nzbInfoUrl: string;
  releaseGroup: string;
  age: string;
  ageHours: string;
  ageMinutes: string;
  publishedDate: string;
  downloadClient: string;
  downloadClientName: string;
  size: string;
  downloadUrl: string;
  guid: string;
  tvdbId: string;
  tvRageId: string;
  protocol: string;
  customFormatScore?: string;
  seriesMatchType: string;
  releaseSource: string;
  indexerFlags: string;
  releaseType: string;
}

export interface DownloadFailedHistory {
  message: string;
  indexer?: string;
  source?: string;
}

export interface DownloadFolderImportedHistory {
  customFormatScore?: string;
  downloadClient: string;
  downloadClientName: string;
  droppedPath: string;
  importedPath: string;
  size: string;
}

export interface EpisodeFileDeletedHistory {
  customFormatScore?: string;
  reason: 'Manual' | 'MissingFromDisk' | 'Upgrade';
  size: string;
}

export interface EpisodeFileRenamedHistory {
  sourcePath: string;
  sourceRelativePath: string;
  path: string;
  relativePath: string;
}

export interface DownloadIgnoredHistory {
  message: string;
}

export type HistoryData =
  | GrabbedHistoryData
  | DownloadFailedHistory
  | DownloadFolderImportedHistory
  | EpisodeFileDeletedHistory
  | EpisodeFileRenamedHistory
  | DownloadIgnoredHistory;

// Sonarr divergence: Phase 15 Plan 15-12 — QualityModel stripped per cascade
// absorption (Plan 15-03). Manga history uses typings/ChapterHistory.ts which
// does not carry a QualityModel field. Phase 8 cleanup: collapse with ChapterHistory.
export default interface History {
  episodeId: number;
  seriesId: number;
  sourceTitle: string;
  languages: Language[];
  quality: unknown;
  customFormats: CustomFormat[];
  customFormatScore: number;
  qualityCutoffNotMet: boolean;
  date: string;
  downloadId: string;
  eventType: HistoryEventType;
  data: HistoryData;
  id: number;
}
