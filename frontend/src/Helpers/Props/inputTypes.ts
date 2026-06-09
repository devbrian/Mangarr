export const AUTO_COMPLETE = 'autoComplete';
export const CAPTCHA = 'captcha';
export const CHECK = 'check';
export const DEVICE = 'device';
export const KEY_VALUE_LIST = 'keyValueList';
export const MONITOR_EPISODES_SELECT = 'monitorEpisodesSelect';
export const FLOAT = 'float';
export const NUMBER = 'number';
export const OAUTH = 'oauth';
export const PASSWORD = 'password';
export const PATH = 'path';
export const QUALITY_PROFILE_SELECT = 'qualityProfileSelect';
// Phase 17 follow-up (debug qualityprofiles-redux-rename, 2026-05-12 — GH #82
// Path 1): manga-canonical select for `translationProfileId` form fields.
// Consumed by the bulk Edit Manga modal (Manga/Index/Select/Edit/) and any
// future manga form referencing the Translation Profile axis.
export const TRANSLATION_PROFILE_SELECT = 'translationProfileSelect';
// quick-260608-vf9 follow-up — manga-canonical select for `customFormatProfileId`
// form fields. Consumed by the import-list Add/Edit modal (defaults to the
// isDefault custom format profile). Sibling of TRANSLATION_PROFILE_SELECT.
export const CUSTOM_FORMAT_PROFILE_SELECT = 'customFormatProfileSelect';
export const INDEXER_SELECT = 'indexerSelect';
export const INDEXER_FLAGS_SELECT = 'indexerFlagsSelect';
export const LANGUAGE_SELECT = 'languageSelect';
export const DOWNLOAD_CLIENT_SELECT = 'downloadClientSelect';
export const ROOT_FOLDER_SELECT = 'rootFolderSelect';
export const SELECT = 'select';
export const SERIES_TAG = 'seriesTag';
export const DYNAMIC_SELECT = 'dynamicSelect';
export const SERIES_TYPE_SELECT = 'seriesTypeSelect';
export const TAG = 'tag';
export const TEXT = 'text';
export const TEXT_AREA = 'textArea';
export const TEXT_TAG = 'textTag';
export const TAG_SELECT = 'tagSelect';
export const UMASK = 'umask';

export const all = [
  AUTO_COMPLETE,
  CAPTCHA,
  CHECK,
  DEVICE,
  KEY_VALUE_LIST,
  MONITOR_EPISODES_SELECT,
  FLOAT,
  NUMBER,
  OAUTH,
  PASSWORD,
  PATH,
  QUALITY_PROFILE_SELECT,
  TRANSLATION_PROFILE_SELECT,
  CUSTOM_FORMAT_PROFILE_SELECT,
  INDEXER_SELECT,
  DOWNLOAD_CLIENT_SELECT,
  ROOT_FOLDER_SELECT,
  LANGUAGE_SELECT,
  SELECT,
  SERIES_TAG,
  DYNAMIC_SELECT,
  SERIES_TYPE_SELECT,
  TAG,
  TEXT,
  TEXT_AREA,
  TEXT_TAG,
  TAG_SELECT,
  UMASK,
];

export type InputType =
  | 'autoComplete'
  | 'captcha'
  | 'check'
  | 'date'
  | 'device'
  | 'keyValueList'
  | 'monitorEpisodesSelect'
  | 'file'
  | 'float'
  | 'number'
  | 'oauth'
  | 'password'
  | 'path'
  | 'qualityProfileSelect'
  | 'translationProfileSelect'
  | 'customFormatProfileSelect'
  | 'indexerSelect'
  | 'indexerFlagsSelect'
  | 'languageSelect'
  | 'downloadClientSelect'
  | 'rootFolderSelect'
  | 'select'
  | 'seriesTag'
  | 'dynamicSelect'
  | 'seriesTypeSelect'
  | 'tag'
  | 'text'
  | 'textArea'
  | 'textTag'
  | 'tagSelect'
  | 'umask';
