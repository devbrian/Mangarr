// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/useSeriesQualityProfile.ts.
//
// Manga sibling preserves: signature shape `(item) => qualityProfile`.
// Manga sibling diverges from useSeriesQualityProfile:
//   * Reads `qualityProfileId` off the Manga (Manga.ts carries the Sonarr-shape
//     `qualityProfileId` field as an optional carry-over per Plan 07-04 Lock #10).
//   * Returns the same TV qualityProfile shape — manga's TranslationProfile is
//     the Phase 5 entity, but the inherited Index columns reference
//     `qualityProfile?.name` for compatibility. A future plan ships
//     useMangaTranslationProfile and replaces this stub.
//
// Phase 8 cleanup: collapse with useSeriesQualityProfile when Tv/ deletes.
import { useQualityProfile } from 'Settings/Profiles/Quality/useQualityProfiles';
import Manga from './Manga';

const useMangaQualityProfile = (manga: Manga | undefined) => {
  return useQualityProfile(manga?.qualityProfileId);
};

export default useMangaQualityProfile;
