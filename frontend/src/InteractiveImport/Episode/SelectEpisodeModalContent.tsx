// Sonarr divergence: Phase 15 Plan 15-12 — STUB.
// Sonarr divergence: Phase 17.3 Plan 17.3-13 (D-09 stub-importer cascade) —
// Episode/Episode rewritten to Chapter/Chapter peer; the subdir name
// `InteractiveImport/Episode/` is preserved (subdir rename deferred to Plan
// 17.3-14 i18n/doc sweep evaluation or a v1.x cleanup). Only the import path
// + the `episodes` field type are migrated here.
import Chapter from 'Chapter/Chapter';

export default function SelectEpisodeModalContent(
  _props: Record<string, unknown>
) {
  return null;
}
export interface SelectedEpisode {
  id: number;
  title?: string;
  episodes?: Chapter[];
  episodeNumber?: number;
  seasonNumber?: number;
}
