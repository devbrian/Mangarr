// Sonarr divergence: REWRITE per Phase 25 Plan 25-04 Task 4 (v1.1-03 +
// Pitfall 2/3 grep gates) — see DIVERGENCE.md.
//
// Replaces the prior TV-shape carry-over interface (Phase 17.3 Plan 17.3-13
// b — preserved seriesId/episodeIds/episodeFileId/seasonNumber/episodes/series
// field names on the runtime DTO because the V5 backend was not yet emitting
// manga peers). Plan 25-02 shipped the V5 ManualImportController +
// ManualImportResource at `/api/v5/manualimport` with manga-shape fields
// (manga, chapters, translatedLanguage, scanlationGroup); this rewrite
// catches the frontend type up to the backend contract.
//
// Shape per 25-PATTERNS.md §`InteractiveImport.ts` (REWRITE typed
// discriminated union v1.1-03) lines 460-531 and RESEARCH §Pattern 3 lines
// 306-376. Discriminator field NAME = `kind` per RESEARCH §Alternatives
// Considered + CONTEXT Claude's Discretion (chosen over `source` /
// `__discriminator` for TypeScript narrowing ergonomics). Three kinds:
//   * 'manga-imported' — row backed by an existing ChapterFile (the
//     "already in your library" detection path). manga + chapters required.
//   * 'queue-source' — row sourced from an active download (downloadId is
//     the cache-key into the queue). downloadId required.
//   * 'folder-source' — row discovered via the folder picker (the
//     `/add/import` page flow + the staging-folder import flow).
//
// existingFileBehavior carries the per-row D-04 dropdown selection
// (Skip default; Replace opt-in). Mirrors the C# ManualImportItem field;
// roundtrips as camelCase string per the STJson global converter.
//
// Plan 25-04 Task 4 grep gates ENFORCED by acceptance criteria:
//   * Pitfall 2 — zero runtime property-presence checks (consumers narrow
//     on `row.kind === ...` literal-string discriminator instead of using
//     the property-presence runtime operator).
//   * Pitfall 3 — zero type-cast escape hatches in
//     frontend/src/InteractiveImport and
//     frontend/src/InteractiveSearch/OverrideMatch.
import ModelBase from 'App/ModelBase';
import Chapter from 'Chapter/Chapter';
import ReleaseType from 'InteractiveImport/ReleaseType';
import Manga from 'Manga/Manga';
import CustomFormat from 'typings/CustomFormat';
import ExistingFileBehavior from 'typings/ExistingFileBehavior';
import Rejection from 'typings/Rejection';

// Typed discriminator union — RESEARCH Pattern 3 + 25-PATTERNS lines
// 480-530. The plain string-literal union is the simplest TypeScript
// narrowing shape; `interface X { kind: 'foo' }` lets `if (row.kind ===
// 'foo')` narrow `row` to `MangaImportedRow` automatically without any
// runtime `in`-check or `as` cast (Pitfalls 2 + 3).
export type ImportSourceKind =
  | 'manga-imported'
  | 'queue-source'
  | 'folder-source';

export interface InteractiveImportBase extends ModelBase {
  kind: ImportSourceKind;
  path: string;
  relativePath: string;
  folderName: string;
  name: string;
  size: number;
  // Per-row D-04 dropdown — defaults to Skip on the backend when missing
  // (preserves no-destructive-default safety). Required at the type level
  // because the V5 controller always emits it on the response.
  existingFileBehavior: ExistingFileBehavior;
  rejections: Rejection[];
}

export interface MangaImportedRow extends InteractiveImportBase {
  kind: 'manga-imported';
  manga: Manga;
  chapters: Chapter[];
  chapterFileId?: number;
  translatedLanguage?: string;
  scanlationGroup?: string;
  customFormats: CustomFormat[];
  customFormatScore: number;
  indexerFlags: number;
  releaseType: ReleaseType;
  downloadId?: string;
}

export interface QueueSourceRow extends InteractiveImportBase {
  kind: 'queue-source';
  manga?: Manga;
  chapters?: Chapter[];
  downloadId: string;
  translatedLanguage?: string;
  scanlationGroup?: string;
  customFormats: CustomFormat[];
  customFormatScore: number;
  indexerFlags: number;
  releaseType: ReleaseType;
}

export interface FolderSourceRow extends InteractiveImportBase {
  kind: 'folder-source';
  manga?: Manga;
  chapters?: Chapter[];
  translatedLanguage?: string;
  scanlationGroup?: string;
  customFormats: CustomFormat[];
  customFormatScore: number;
  indexerFlags: number;
  releaseType: ReleaseType;
}

type InteractiveImport = MangaImportedRow | QueueSourceRow | FolderSourceRow;

export default InteractiveImport;

// Command-options surface — sent to backend ManualImport command per row.
// Manga-shape (mangaId / chapterIds) per Plan 25-02 V5 controller +
// Plan 25-04 Task 4 + Task 7 wiring (D-04 ExistingFileBehavior end-to-end).
export interface InteractiveImportCommandOptions {
  path: string;
  folderName: string;
  mangaId: number;
  chapterIds: number[];
  scanlationGroup?: string;
  translatedLanguage?: string;
  indexerFlags: number;
  releaseType: ReleaseType;
  downloadId?: string;
  chapterFileId?: number;
  existingFileBehavior?: ExistingFileBehavior;
}
