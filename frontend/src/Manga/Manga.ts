// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Series.ts.
//
// Manga sibling preserves: ModelBase + Tags + Images + Monitored.
// Manga sibling diverges from Series:
//   * MangaMonitor has 5 values (Phase 6 D-03), not 9 like SeriesMonitor.
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
// Phase 8 cleanup: collapse with Series when Tv/ deletes.
import ModelBase from 'App/ModelBase';

export type MangaMonitor =
  | 'all'
  | 'future'
  | 'missing'
  | 'latest'
  | 'none';

export type MangaStatus =
  | 'ongoing'
  | 'hiatus'
  | 'completed'
  | 'cancelled'
  | 'unknown';

export interface MangaImage {
  coverType: 'poster' | 'banner' | 'fanart' | 'cover' | 'screenshot';
  url: string;
  remoteUrl?: string;
}

interface Manga extends ModelBase {
  title: string;
  sortTitle: string;
  cleanTitle?: string;
  titleSlug?: string;
  status: MangaStatus;
  contentRating?: string;
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
  monitored: boolean;
  monitor?: MangaMonitor;
  tags: number[];
  images: MangaImage[];
  // Singular per Phase 2 02-CONTEXT (manga is 1:1 across sources, unlike anime).
  mangaDexId?: string;
  aniListId?: number;
  malId?: number;
  // Plural arrays carried for future-multi-source extension; preferred shape per
  // plan must_haves §truths (downstream Plans 07-04..07-10 may reference either).
  aniListIds?: number[];
  malIds?: number[];
  genres?: string[];
  upgradeAllowedOverride?: boolean;
}

export default Manga;
