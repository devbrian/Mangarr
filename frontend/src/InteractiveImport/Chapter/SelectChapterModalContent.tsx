// Sonarr divergence: Phase 15 Plan 15-12 — STUB.
// Sonarr divergence: Phase 17.3 Plan 17.3-13 (D-09 stub-importer cascade) —
// Episode/Episode rewritten to Chapter/Chapter peer.
// Sonarr divergence: Phase 25 Plan 25-04 Task 1 — subdir rename
// `InteractiveImport/Episode/` → `InteractiveImport/Chapter/` (Pitfall 13
// atomic-per-decision commit). Identifiers SelectEpisodeModalContent →
// SelectChapterModalContent; SelectedEpisode → SelectedChapter. The
// `episodes` field NAME on the interface is preserved here — the broader
// TV-shape field rename (episodes → chapters, etc.) is the responsibility
// of Plan 25-04 Task 4's typed discriminator union rewrite.
import Chapter from 'Chapter/Chapter';

export default function SelectChapterModalContent(
  _props: Record<string, unknown>
) {
  return null;
}
export interface SelectedChapter {
  id: number;
  title?: string;
  episodes?: Chapter[];
  episodeNumber?: number;
  seasonNumber?: number;
}
