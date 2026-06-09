// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Series.ts.
//
// Manga sibling preserves: ModelBase + Tags + Images + Monitored.
// Manga sibling diverges from Series:
//   * MangaMonitor has 7 values (#357 D-2, supersedes the Phase 6 D-03 5-value lock),
//     not 9 like SeriesMonitor.
//   * No SeriesType / Network / QualityProfileId / Season-folder fields.
//   * Adds TranslationProfileId, CustomFormatProfileId, AniListIds, MalIds, ContentRating.
//
// Forward-looking shape note: the Phase 2 MangaResource baseline has SINGULAR
// `mangaDexId`/`malId`/`aniListId` and does NOT yet expose `monitor` / `titleSlug` /
// `year` (uses `publicationYear`). This Manga.ts type carries the *target* shape
// downstream Plans 07-04..07-10 will consume — fields the current backend does not
// emit are marked optional so consumers can read them defensively until the
// MangaResource is extended. See 07-03-SUMMARY.md "Forward-looking shape" note.
//
// Phase 17.3 D-13 completed (2026-05-11): the Sonarr-shape carry-over fields
// (network/seasons/seriesType/seasonFolder/firstAired/.../tvdbId/etc.) have
// been removed from the Manga interface per the user's "v1 ships free of TV
// vocabulary" lock. The Wave 4 per-component forks (Plans 17.3-08..12) drop
// the corresponding consumer references. Statistics carries chapter-shape
// progress numbers only; the Season interface was deleted (manga has no
// seasons per DOMAIN-02).
import ModelBase from 'App/ModelBase';
import ReleaseType from 'InteractiveImport/ReleaseType';

export type MangaMonitor =
  | 'all'
  | 'future'
  | 'missing'
  | 'existing'
  | 'first'
  | 'latest'
  | 'none';

export type MangaStatus =
  | 'ongoing'
  | 'hiatus'
  | 'completed'
  | 'cancelled'
  | 'unknown';

export type MangaType = 'manga' | 'manhwa' | 'manhua' | 'oneshot';

export type CoverType = 'poster' | 'banner' | 'fanart' | 'cover' | 'screenshot';

export interface MangaImage {
  coverType: CoverType;
  url: string;
  remoteUrl?: string;
}

// Backwards-compat alias so inherited Sonarr components that import { Image,
// CoverType } from the manga module type-check without a code rewrite.
export type Image = MangaImage;

// Statistics carries chapter-shape progress numbers (parallel to Sonarr's
// per-series Statistics with episode counts). Fields are optional because the
// Phase 2 MangaResource baseline does not emit them yet; Phase 8 cutover lands
// the canonical shape.
export interface Statistics {
  chapterCount?: number;
  chapterFileCount?: number;
  totalChapterCount?: number;
  monitoredChapterCount?: number;
  percentOfChapters?: number;
  releaseGroups?: string[];
  releaseTypes?: ReleaseType[];
  sizeOnDisk?: number;
}

export interface Ratings {
  votes: number;
  value: number;
}

export interface AlternateTitle {
  title: string;
  comment?: string;
}

export interface MangaAddOptions {
  monitor: MangaMonitor;
  searchForMissingChapters: boolean;
}

interface Manga extends ModelBase {
  title: string;
  sortTitle: string;
  cleanTitle?: string;
  titleSlug?: string;
  status: MangaStatus;
  contentRating?: string;
  certification?: string;
  overview?: string;
  added?: string;
  lastInfoSync?: string;
  year?: number;
  publicationYear?: number;
  primaryAuthor?: string;
  totalChapterCount?: number;
  path: string;
  rootFolderPath?: string;
  translationProfileId?: number;
  customFormatProfileId?: number;
  qualityProfileId?: number;
  monitored: boolean;
  monitor?: MangaMonitor;
  tags: number[];
  images: MangaImage[];
  // Singular per Phase 2 02-CONTEXT (manga is 1:1 across sources, unlike anime).
  mangaDexId?: string;
  aniListId?: number;
  malId?: number;
  mangaBakaId?: number;
  // quick-260608-l2e — additional MangaBaka cross-source ids (source.* block).
  kitsuId?: number;
  animeNewsNetworkId?: number;
  shikimoriId?: number;
  animePlanetId?: string;
  mangaUpdatesId?: string;
  // Plural arrays carried for future-multi-source extension; preferred shape per
  // plan must_haves §truths (downstream Plans 07-04..07-10 may reference either).
  aniListIds?: number[];
  malIds?: number[];
  genres?: string[];
  ratings?: Ratings;
  alternateTitles?: AlternateTitle[];
  upgradeAllowedOverride?: boolean;
  statistics?: Statistics;
  addOptions?: MangaAddOptions;
}

export default Manga;
