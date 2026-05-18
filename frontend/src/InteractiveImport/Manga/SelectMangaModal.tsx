// Sonarr divergence: Phase 15 Plan 15-12 — STUB.
// Sonarr divergence: Phase 25 Plan 25-04 Task 2 — subdir rename
// `InteractiveImport/Series/` → `InteractiveImport/Manga/` (Pitfall 13
// atomic-per-decision commit). Component identifier SelectSeriesModal →
// SelectMangaModal. Body remains a no-op stub per Plan 15-12; the
// TV-shape series-picker flow is gated for v1 per Phase 12 Plan 12-11
// LOCK guard and replaced by the manga-shape discriminator union in
// Plan 25-04 Task 4.
export default function SelectMangaModal(_props: Record<string, unknown>) {
  return null;
}
