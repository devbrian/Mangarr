// Phase 7 Plan 07-05 — Manga sibling per D-07 reuse of existing InteractiveSearch
// component. Phase 6 D-08 ranked-modal pattern reused — chapter-shape variant
// added here.
//
// Sonarr divergence: NEW manga payload variants per Phase 7 D-07 — see DIVERGENCE.md.
// Existing TV variants preserved verbatim. RESEARCH Lock #14 governs the union shape.
//
// Phase 8 cleanup: collapse Episode/Season variants when Tv/ deletes; the file
// becomes manga-only.

// Plan 25-04 Task 5 (v1.1-03 + v1.1-04) — added `kind` literal-string
// discriminator field to all 4 variants. Consumers narrow on
// `payload.kind === '…'` instead of the property-presence runtime
// operator (Pitfall 2). The discriminator field name `kind` matches the
// InteractiveImport.ts typed-union convention from Plan 25-04 Task 4.
interface EpisodeSearchPayload {
  kind: 'episode';
  episodeId: number;
}

interface SeasonSearchPayload {
  kind: 'season';
  seriesId: number;
  seasonNumber: number;
}

// NEW Phase 7 (Plan 07-05):
interface ChapterSearchPayload {
  kind: 'chapter';
  chapterId: number;
}

interface MangaSearchPayload {
  kind: 'manga';
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
