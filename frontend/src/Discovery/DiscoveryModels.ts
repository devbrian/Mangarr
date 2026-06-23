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

// POST /discovery/search body — the wire contract the 42-04 controller
// (DiscoverySearchRequestResource) deserializes. NOT the store shape: the
// tristate maps are split into include/exclude string arrays, the tag selections
// into integer tag/tagNot id arrays, and topX maps to `x`. Sending the raw store
// shape (a TristateMap `{}` into a `List<string>`) fails JSON binding → 400.
export interface DiscoverySearchRequest {
  type: string[];
  typeNot: string[];
  genre: string[];
  genreNot: string[];
  tag: number[];
  tagNot: number[];
  tagMode: 'and' | 'or';
  status: string[];
  statusNot: string[];
  contentRating: string[];
  includeAdult: boolean;
  yearLower?: number;
  yearUpper?: number;
  ratingLower?: number;
  ratingUpper?: number;
  sortBy: string;
  x: number;
}

function splitTristate(map: TristateMap): {
  include: string[];
  exclude: string[];
} {
  const include: string[] = [];
  const exclude: string[] = [];

  Object.entries(map).forEach(([key, mode]) => {
    if (mode === 'include') {
      include.push(key);
    } else if (mode === 'exclude') {
      exclude.push(key);
    }
  });

  return { include, exclude };
}

// Map the persisted store shape to the controller's wire contract.
// ContentRating has no exclude on the wire (the backend whitelists only the
// include list — MangaBaka's content_rating has no _not pair); exclude chips on
// ContentRating are intentionally not forwarded.
export function toDiscoverySearchRequest(
  options: DiscoveryOptions
): DiscoverySearchRequest {
  const type = splitTristate(options.type);
  const genre = splitTristate(options.genre);
  const status = splitTristate(options.status);
  const contentRating = splitTristate(options.contentRating);

  return {
    type: type.include,
    typeNot: type.exclude,
    genre: genre.include,
    genreNot: genre.exclude,
    tag: options.tags.filter((t) => t.mode === 'include').map((t) => t.id),
    tagNot: options.tags.filter((t) => t.mode === 'exclude').map((t) => t.id),
    tagMode: options.tagMode,
    status: status.include,
    statusNot: status.exclude,
    contentRating: contentRating.include,
    includeAdult: options.includeAdult,
    yearLower: options.yearLower,
    yearUpper: options.yearUpper,
    ratingLower: options.ratingLower,
    ratingUpper: options.ratingUpper,
    sortBy: options.sortBy,
    x: options.topX,
  };
}

export interface DiscoveryResult {
  mangaBakaId: number;
  title: string;
  coverUrl?: string;
  year?: number;
  status?: string;
  type?: string;
  contentRating?: string;
  genres: string[];
  description?: string;
  tags: string[];
  score?: number;
}

export interface DiscoverySearchResponse {
  results: DiscoveryResult[];
  poolExhausted: boolean;
  requested: number;
  found: number;
  // Toolbar summary figures (sketch 001).
  totalMatch: number;
  hiddenInLibrary: number;
  hiddenExcluded: number;
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
