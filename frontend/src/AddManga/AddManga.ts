// Sonarr divergence: NEW manga sibling per Phase 7 D-01 / D-04 — see DIVERGENCE.md.
// Role-match analog: frontend/src/AddSeries/AddSeries.ts.
//
// Manga sibling preserves: lookup-result + add-payload split. Manga sibling diverges:
//   * Lookup-result fields surfaced by GET /api/v5/manga/lookup (MangaDex / AniList /
//     MAL identifiers; manga is 1:1 across sources per Phase 2 02-CONTEXT specifics).
//   * Payload uses 5-value MangaMonitor (Phase 6 D-03), translationProfileId
//     (replaces qualityProfileId), customFormatProfileId (NEW), and
//     searchForMissingChapters (Phase 6 D-06 SearchOnAdd).
//   * No seriesType / seasonFolder / searchForCutoffUnmetEpisodes.
//
// Phase 8 cleanup: collapse with AddSeries when AddSeries/ deletes.
import { MangaImage, MangaMonitor, MangaStatus } from 'Manga/Manga';

export interface AddMangaResult {
  // Lookup result fields surfaced by GET /api/v5/manga/lookup
  title: string;
  titleSlug?: string;
  status: MangaStatus;
  overview?: string;
  year?: number;
  publicationYear?: number;
  primaryAuthor?: string;
  images: MangaImage[];
  // External IDs — singular per Phase 2 02-CONTEXT (manga is 1:1 across sources).
  mangaDexId?: string;
  aniListId?: number;
  malId?: number;
  // Optional preview metadata (display-only on lookup grid).
  totalChapterCount?: number;
  isExcluded?: boolean;
}

export interface AddMangaPayload {
  // POST /api/v5/manga body shape
  title: string;
  titleSlug?: string;
  rootFolderPath: string;
  monitor: MangaMonitor;
  translationProfileId: number;
  customFormatProfileId: number;
  tags: number[];
  searchForMissingChapters: boolean;
  // Pass-through identifiers — singular per Phase 2 02-CONTEXT.
  mangaDexId?: string;
  aniListId?: number;
  malId?: number;
}

export default AddMangaResult;
