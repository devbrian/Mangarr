// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/Profiles/Quality/useQualityProfiles.ts (data-list hook only).
//
// Phase 17 follow-up (debug `qualityprofiles-redux-rename`, 2026-05-12 — GH #82
// Path 1 surface-rename cascade): exposes a `useTranslationProfilesData()`
// helper that wraps `useApiQuery<TranslationProfileResource[]>` against
// `/api/v5/translationprofile` so the renamed `TranslationProfileSelectInput`
// and `TranslationProfileFilterBuilderRowValue` consumers can read the
// profile list without colliding with the now-deleted
// `/api/v5/qualityprofile` endpoint (removed in Phase 15 Plan 15-03 D-12).
// The full `useManageTranslationProfile` / `useDeleteTranslationProfile`
// surface is intentionally NOT ported here — the canonical TranslationProfile
// editor (Settings/Profiles/Translations/EditTranslationProfileModalContent.tsx)
// owns the mutation flow via direct `useApiMutation` calls; this hook only
// powers the read-side select + filter consumers.
//
// Phase 8 cleanup: this stays — manga-canonical. Future Phase 8 collapse can
// merge this with `TranslationProfileName.tsx`'s `useTranslationProfileName`
// since both query the same path.
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { TranslationProfileResource } from './TranslationProfile';

const PATH = '/translationprofile';

export const useTranslationProfiles = () => {
  const result = useApiQuery<TranslationProfileResource[]>({
    path: PATH,
    queryOptions: {
      gcTime: Infinity,
      staleTime: 5 * 60 * 1000,
    },
  });

  return {
    ...result,
    data: result.data ?? ([] as TranslationProfileResource[]),
  };
};

export const useTranslationProfilesData = () => {
  const { data } = useTranslationProfiles();

  return data;
};

export const useTranslationProfile = (id: number | undefined) => {
  const { data } = useTranslationProfiles();

  if (id === undefined) {
    return undefined;
  }

  return data.find((profile) => profile.id === id);
};
