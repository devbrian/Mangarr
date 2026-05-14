// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/Profiles/Quality/QualityProfileName.tsx (lines 1-15).
//
// UI-07: Name resolver for `translationProfileId` columns referenced from Plan 07-04 manga column array
// (Plan 07-05 ChapterRow / MangaIndex use this) wiring /api/v5/translationprofile.
//
// Manga sibling preserves: simple <span> name resolution + 'Unknown' fallback.
//
// Manga sibling diverges from QualityProfileName:
//   * Wires /api/v5/translationprofile (Phase 5 Plan 05-02) instead of /api/v5/qualityprofile
//   * Exposes `useTranslationProfileName` hook variant for callers that want the raw name string
//
// Phase 8 cleanup: this stays — manga-canonical.

import React from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import translate from 'Utilities/String/translate';
import { TranslationProfileResource } from './TranslationProfile';

const PATH = '/translationprofile';

export function useTranslationProfileName(
  id: number | undefined
): string | undefined {
  const { data } = useApiQuery<TranslationProfileResource[]>({
    path: PATH,
    queryOptions: {
      gcTime: Infinity,
      staleTime: 5 * 60 * 1000,
    },
  });

  if (id === undefined || !data) {
    return undefined;
  }

  return data.find((p) => p.id === id)?.name;
}

interface TranslationProfileNameProps {
  translationProfileId: number;
}

function TranslationProfileName({
  translationProfileId,
}: TranslationProfileNameProps) {
  const name = useTranslationProfileName(translationProfileId);

  return <span>{name ?? translate('Unknown')}</span>;
}

export default TranslationProfileName;
