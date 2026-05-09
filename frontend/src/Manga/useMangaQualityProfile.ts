// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/useSeriesQualityProfile.ts.
//
// Manga sibling preserves: signature shape `(item) => qualityProfile`.
// Manga sibling diverges from useSeriesQualityProfile:
//   * Reads `qualityProfileId` off the Manga (Manga.ts carries the Sonarr-shape
//     `qualityProfileId` field as an optional carry-over per Plan 07-04 Lock #10).
//   * Returns `undefined` directly — manga has no quality model (Phase 5 D-01
//     deleted QualityProfileController; Phase 5 D-04 deferred a manga-shape
//     replacement; Phase 8 collapses with useSeriesQualityProfile when Tv/ deletes).
//     Previously delegated to `useQualityProfile`, which fired
//     `GET /api/v5/qualityprofile` on every MangaIndex row mount → 404 (the
//     backend controller was removed by the Phase 5 cascade). The inherited
//     Index columns (`MangaIndexPosterInfo` / `MangaIndexOverviewInfo` /
//     `MangaIndexRow`) reference `qualityProfile?.name` and gracefully render
//     empty when undefined, so the stub preserves the row-shape contract
//     without round-tripping a dead route.
//
// Phase 8 cleanup: collapse with useSeriesQualityProfile when Tv/ deletes (or
// replace with useMangaTranslationProfile when Phase 5 D-04 ships).
import { QualityProfileModel } from 'Settings/Profiles/Quality/useQualityProfiles';
import Manga from './Manga';

const useMangaQualityProfile = (
  _manga: Manga | undefined
): QualityProfileModel | undefined => {
  return undefined;
};

export default useMangaQualityProfile;
