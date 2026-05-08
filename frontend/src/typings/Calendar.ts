// Sonarr divergence: Phase 15 Plan 15-12 — Episode/Episode import stripped per
// cascade absorption (Plan 15-07 deleted Episode subtree). CalendarItem now carries
// the minimal field set the legacy Calendar component reads; manga calendar uses
// Chapter/Chapter directly. Phase 8 cleanup: collapse with manga calendar resource.

export interface CalendarItem {
  id: number;
  airDateUtc: string;
  seriesId?: number;
  seasonNumber?: number;
  episodeNumber?: number;
  title?: string;
  monitored?: boolean;
  hasFile?: boolean;
  // Loose extension allowed so verbatim-inherited Calendar components compile
  // while reading undefined defensively (Sonarr-shape carry-over per design philosophy).
  [key: string]: unknown;
}

export interface CalendarEvent extends CalendarItem {
  isGroup: false;
}

export interface CalendarEventGroup {
  isGroup: true;
  seriesId: number;
  seasonNumber: number;
  episodeIds: number[];
  events: CalendarItem[];
}

export type CalendarStatus =
  | 'downloaded'
  | 'downloading'
  | 'unmonitored'
  | 'onAir'
  | 'missing'
  | 'unaired';
