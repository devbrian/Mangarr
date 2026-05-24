// Sonarr divergence: Phase 15 Plan 15-12 — STUB.
// Sonarr divergence: Phase 17.3 Plan 17.3-13 (D-09 stub-importer cascade) —
// Episode/Episode rewritten to Chapter/Chapter peer.
// Sonarr divergence: Phase 25 Plan 25-04 Task 1 — subdir rename
// `InteractiveImport/Episode/` → `InteractiveImport/Chapter/` (Pitfall 13
// atomic-per-decision commit). Identifiers SelectEpisodeModalContent →
// SelectChapterModalContent; SelectedEpisode → SelectedChapter.
// Sonarr divergence: Phase 30 Plan 30-01 Task 1 (II2-04) — TV-shape field
// rename catch-up per CONTEXT.md `<domain>` §4 + PATTERNS.md §Plan 30-01:
// episodes? -> chapters?, episodeNumber? -> chapterNumber?, seasonNumber?
// DROPPED (no Season concept in manga per PROJECT.md DOMAIN-02). Atomically
// propagated to all SelectedChapter consumers in this commit.
import Chapter from 'Chapter/Chapter';

export default function SelectChapterModalContent(
  _props: Record<string, unknown>
) {
  return null;
}
export interface SelectedChapter {
  id: number;
  title?: string;
  chapters?: Chapter[];
  chapterNumber?: number;
}
