// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// authored as the manga-shape peer of the deleted Utilities/Series/monitorNewItemsOptions
// stub. Body matches the Phase 15 Plan 15-12 STUB shape (empty array). MonitorNewItems
// (Manga.ts:43) currently has 2 values ('all' | 'none'); options array remains empty
// until upstream wiring populates it. v1.x cleanup ticket.
const monitorNewItemsOptions: { key: string; value: string }[] = [];
export default monitorNewItemsOptions;
