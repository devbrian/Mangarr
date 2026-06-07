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
  // mangaBakaId added Phase 41 (MangaBaka default-primary source).
  mangaBakaId?: number;
  mangaDexId?: string;
  aniListId?: number;
  malId?: number;
  // Optional preview metadata (display-only on lookup grid).
  totalChapterCount?: number;
  isExcluded?: boolean;
}

// Bug fix new-manga-default-monitored (2026-05-08): wire-shape mirror of
// backend `AddMangaOptionsResource` (Mangarr.Api.V5/Manga/MangaResource.cs).
// Carries the post-add per-Chapter Monitor cascade choices through the AddManga
// POST so MangaScannedHandler.SetChapterMonitoredStatus runs on the new manga.
// Without this nested field on the wire, the backend MangaResource silently
// dropped the flat `monitor` / `searchForMissingChapters` keys at JSON
// deserialization → Manga.AddOptions = null → SetChapterMonitoredStatus
// bypass at MangaScannedHandler.cs:65-71.
export interface AddMangaOptionsPayload {
  monitor: MangaMonitor;
  searchForMissingChapters: boolean;
  searchForCutoffUnmetChapters?: boolean;
  ignoreChaptersWithFiles?: boolean;
  ignoreChaptersWithoutFiles?: boolean;
}

export interface AddMangaPayload {
  // POST /api/v5/manga body shape
  title: string;
  titleSlug?: string;
  rootFolderPath: string;
  // Bug fix new-manga-default-monitored (2026-05-08): explicitly send
  // `monitored: true` on Add so backend MangaResource.Monitored is true
  // before AddMangaService.PrepareForAdd runs (the backend MangaController
  // also defaults Monitored=true unless AddOptions.Monitor=None — defense
  // in depth: send explicit truthy from the UI side too).
  monitored: boolean;
  monitor: MangaMonitor;
  // Bug fix new-manga-default-monitored (2026-05-08): nested addOptions object
  // mirrors the backend `AddMangaOptionsResource` so MangaResourceMapper.ToModel
  // can populate Manga.AddOptions. Without it the per-Chapter monitor cascade
  // never runs.
  addOptions: AddMangaOptionsPayload;
  translationProfileId: number;
  customFormatProfileId: number;
  tags: number[];
  searchForMissingChapters: boolean;
  // Pass-through identifiers — singular per Phase 2 02-CONTEXT.
  // mangaBakaId added Phase 41 (MangaBaka default-primary source).
  mangaBakaId?: number;
  mangaDexId?: string;
  aniListId?: number;
  malId?: number;
}
