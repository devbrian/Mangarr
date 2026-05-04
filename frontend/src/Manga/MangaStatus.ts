// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/SeriesStatus.ts (utility helpers, no enum file).
//
// Manga sibling preserves: small enum-utilities pattern. Manga diverges by enumerating
// the manga-domain status set ('ongoing' | 'hiatus' | 'completed' | 'cancelled' |
// 'unknown') — TV's 'continuing' / 'ended' / 'upcoming' / 'deleted' do not apply.
//
// Phase 8 cleanup: collapse with SeriesStatus.ts when Series/ deletes.
import { MangaStatus } from './Manga';

export const MANGA_STATUS_VALUES: MangaStatus[] = [
  'ongoing',
  'hiatus',
  'completed',
  'cancelled',
  'unknown',
];

export function isMonitorableStatus(status: MangaStatus): boolean {
  return status === 'ongoing' || status === 'hiatus';
}
