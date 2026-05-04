// Phase 7 Plan 07-05 — Manga sibling per D-07 reuse of existing InteractiveSearch
// component. Phase 6 D-08 ranked-modal pattern reused — chapter-shape variant
// added here.
//
// Sonarr divergence: NEW manga payload variants per Phase 7 D-07 — see DIVERGENCE.md.
// Existing TV variants preserved verbatim. RESEARCH Lock #14 governs the union shape.
//
// Phase 8 cleanup: collapse Episode/Season variants when Tv/ deletes; the file
// becomes manga-only.

interface EpisodeSearchPayload {
  episodeId: number;
}

interface SeasonSearchPayload {
  seriesId: number;
  seasonNumber: number;
}

// NEW Phase 7 (Plan 07-05):
interface ChapterSearchPayload {
  chapterId: number;
}

interface MangaSearchPayload {
  mangaId: number;
}

type InteractiveSearchPayload =
  | EpisodeSearchPayload
  | SeasonSearchPayload
  | ChapterSearchPayload
  | MangaSearchPayload;

export default InteractiveSearchPayload;
export type {
  EpisodeSearchPayload,
  SeasonSearchPayload,
  ChapterSearchPayload,
  MangaSearchPayload,
};
