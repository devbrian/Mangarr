// Manga-shape discriminated union for what to search. `kind` is the
// literal-string discriminator (Pitfall 2 grep gate); consumers narrow on
// `payload.kind === '…'` instead of runtime property-presence checks. The
// discriminator field name `kind` matches the InteractiveImport.ts typed-union
// convention. The TV-shape Episode/Season variants were retired with the
// OverrideMatch TV fallback (issue #263).
interface ChapterSearchPayload {
  kind: 'chapter';
  chapterId: number;
}

interface MangaSearchPayload {
  kind: 'manga';
  mangaId: number;
}

type InteractiveSearchPayload = ChapterSearchPayload | MangaSearchPayload;

export default InteractiveSearchPayload;
export type { ChapterSearchPayload, MangaSearchPayload };
