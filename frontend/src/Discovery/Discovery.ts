// Phase 42 Plan 42-05 — NEW-in-Mangarr Discovery surface (no Sonarr analog).
// Role-match analog: frontend/src/AddManga/AddManga.ts (TS type bundle for a
// feature module). Discovery builds a filtered query against MangaBaka's REMOTE
// attribute search; these types target the locked 42-04 controller contract
// (42-RESEARCH §Endpoints) — the frontend does NOT import backend types.
//
// Tristate filter maps (D-03/D-09): a key present with 'include' or 'exclude'
// is active; absent keys are neutral/off. Only filters + topX persist
// (discoveryOptionsStore, D-09); results live in volatile query state.

export type TristateMode = 'include' | 'exclude';

export type TristateMap = Record<string, TristateMode>;

export interface DiscoveryTagSelection {
  id: number;
  name: string;
  mode: TristateMode;
}

export interface DiscoveryFilterState {
  type: TristateMap;
  genre: TristateMap;
  status: TristateMap;
  contentRating: TristateMap;
  tags: DiscoveryTagSelection[];
  tagMode: 'and' | 'or';
  includeAdult: boolean;
  yearLower?: number;
  yearUpper?: number;
  ratingLower?: number;
  ratingUpper?: number;
  sortBy: string;
}

export type DiscoveryOptions = DiscoveryFilterState & {
  topX: number;
};

// POST /discovery/search body — the active filter + the requested count.
export type DiscoverySearchRequest = DiscoveryFilterState & {
  topX: number;
};

export interface DiscoveryResult {
  mangaBakaId: number;
  title: string;
  coverUrl?: string;
  year?: number;
  status?: string;
  type?: string;
  contentRating?: string;
  genres: string[];
  score?: number;
}

export interface DiscoverySearchResponse {
  results: DiscoveryResult[];
  poolExhausted: boolean;
  requested: number;
  found: number;
}

export interface DiscoveryGenre {
  value: string;
  label: string;
}

export interface DiscoveryTag {
  id: number;
  name: string;
  namePath: string;
  seriesCount: number;
  contentRating: string;
}

// POST /discovery/bulk-add body — the grid's MangaBakaIds + shared add-options
// (mirrors the single-add AddMangaPayload shape; backend allow-list at 42-04).
export interface DiscoveryBulkAddPayload {
  mangaBakaIds: number[];
  rootFolderPath: string;
  monitor: string;
  translationProfileId: number;
  customFormatProfileId: number;
  tags: number[];
  searchForMissingChapters: boolean;
}
