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

// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 atomic delete fix-forward)
// — authored as the manga-shape peer of the deleted
// Series/SeriesStatus.getSeriesStatusDetails stub (returned generic { title,
// message, icon } no-op shape). Returns manga-domain status copy + icon
// keyed by MangaStatus value. Consumer:
// Manga/Index/Table/MangaStatusCell.tsx.
export interface MangaStatusDetails {
  title: string;
  message: string;
  icon: string;
}

export function getMangaStatusDetails(status: MangaStatus): MangaStatusDetails {
  switch (status) {
    case 'ongoing':
      return {
        title: 'Ongoing',
        message: 'New chapters expected',
        icon: 'rss',
      };
    case 'hiatus':
      return {
        title: 'Hiatus',
        message: 'Publication paused; new chapters may resume',
        icon: 'pause',
      };
    case 'completed':
      return {
        title: 'Completed',
        message: 'Publication finished',
        icon: 'check',
      };
    case 'cancelled':
      return {
        title: 'Cancelled',
        message: 'Publication discontinued',
        icon: 'ban',
      };
    case 'unknown':
    default:
      return {
        title: 'Unknown',
        message: 'Publication status unknown',
        icon: 'question',
      };
  }
}
