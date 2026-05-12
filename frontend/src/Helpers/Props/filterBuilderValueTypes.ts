export const BOOL = 'bool';
export const BYTES = 'bytes';
export const DATE = 'date';
export const DEFAULT = 'default';
export const HISTORY_EVENT_TYPE = 'historyEventType';
export const INDEXER = 'indexer';
export const LANGUAGE = 'language';
export const PROTOCOL = 'protocol';
export const QUALITY = 'quality';
export const QUALITY_PROFILE = 'qualityProfile';
// Phase 17 follow-up (debug qualityprofiles-redux-rename, 2026-05-12 — GH #82
// Path 1): manga-canonical filter-value token for the Translation Profile
// axis. Filter builder rows registered against this token render the new
// `TranslationProfileFilterBuilderRowValue` component (wires `/translationprofile`).
export const TRANSLATION_PROFILE = 'translationProfile';
export const QUEUE_STATUS = 'queueStatus';
export const MONITORED_STATUS = 'monitoredStatus';
export const RELEASE_TYPES = 'releaseTypes';
export const SERIES = 'series';
export const SERIES_STATUS = 'seriesStatus';
export const SERIES_TYPES = 'seriesType';
export const TAG = 'tag';

export type FilterBuildValueType =
  | 'bool'
  | 'bytes'
  | 'date'
  | 'default'
  | 'historyEventType'
  | 'indexer'
  | 'language'
  | 'protocol'
  | 'quality'
  | 'qualityProfile'
  | 'translationProfile'
  | 'queueStatus'
  | 'monitoredStatus'
  | 'releaseTypes'
  | 'series'
  | 'seriesStatus'
  | 'seriesType'
  | 'tag';
