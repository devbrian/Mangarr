// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// authored as the manga-shape peer of the deleted Utilities/Series/monitorOptions
// stub. Body matches the Phase 15 Plan 15-12 STUB shape (empty array). The
// eventual 5-value MangaMonitor option set (all/future/missing/latest/none per
// Manga.ts:27-32) is sourced upstream — this module remains a no-op until that
// wiring lands. v1.x cleanup ticket: populate with translated MangaMonitor
// labels keyed off Manga.ts MangaMonitor type.
const monitorOptions: { key: string; value: string }[] = [];
export default monitorOptions;
