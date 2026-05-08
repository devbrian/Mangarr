// Sonarr divergence: Phase 15 Plan 15-12 — STUB. Manga has no seasons.
export default function formatSeason(seasonNumber?: number): string {
  if (seasonNumber == null) return '';
  return seasonNumber === 0 ? 'Specials' : 'Season ' + seasonNumber;
}
