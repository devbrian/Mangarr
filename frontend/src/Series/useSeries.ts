// Sonarr divergence: Phase 15 Plan 15-12 — thin re-export of Manga/useManga so
// legacy TV-shape consumers compile. Phase 8 cleanup: drop when consumers migrate.
import useManga, {
  useSingleManga,
  useMultipleManga,
  useHasManga,
} from 'Manga/useManga';

export const useSingleSeries = useSingleManga;
export const useMultipleSeries = useMultipleManga;
export const useHasSeries = useHasManga;

export default useManga;
