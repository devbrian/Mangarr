// Sonarr divergence: Phase 15 Plan 15-12 — useSeries -> useManga rebind per
// cascade absorption (Plan 15-07 deleted Series subtree). The hook reports
// counts of manga referencing the QualityProfile; importLists count preserved
// from the legacy Redux slice. Phase 8 cleanup: collapse with Translation/CustomFormat profile in-use checks.
import { useMemo } from 'react';
import { useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import useManga from 'Manga/useManga';

function useQualityProfileInUse(id: number | undefined) {
  const { data: manga = [] } = useManga();
  const importLists = useSelector(
    (state: AppState) => state.settings.importLists.items
  );

  return useMemo(() => {
    if (!id) {
      return {
        seriesCount: 0,
        importsCount: 0,
      };
    }

    return {
      seriesCount: manga.filter((m) => m.qualityProfileId === id).length,
      importListCount: importLists.filter(
        (list) => list.qualityProfileId === id
      ).length,
    };
  }, [id, manga, importLists]);
}

export default useQualityProfileInUse;
