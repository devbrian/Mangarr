// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Index/useSeriesIndexItem.ts.
//
// Manga sibling preserves: composes useSingleManga + useMangaQualityProfile
// + useCommandExecuting for refresh/search executing flags + a `latestSeason`
// derivation (manga has no seasons; latestSeason is always undefined here for
// shape compatibility — the inherited row still references it but renders an
// em-dash in the seasonCount column).
//
// Phase 8 cleanup: collapse with useSeriesIndexItem when Tv/ deletes.
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting } from 'Commands/useCommands';
import { Season } from 'Manga/Manga';
import { useSingleManga } from 'Manga/useManga';
import useMangaQualityProfile from 'Manga/useMangaQualityProfile';

export function useMangaIndexItem(mangaId: number) {
  const manga = useSingleManga(mangaId);
  const qualityProfile = useMangaQualityProfile(manga);

  const isRefreshingManga = useCommandExecuting(CommandNames.RefreshManga, {
    mangaIds: [mangaId],
  });

  const isSearchingManga = useCommandExecuting(CommandNames.MangaSearch, {
    mangaId,
  });

  // Manga has no seasons (Plan 07-04 Lock #10). `latestSeason` always undefined
  // — preserved as a return-shape carry-over so inherited row code compiles.
  // Typed via the explicit return shape below to defeat TS narrowing past the
  // initial `undefined` so callers can still access `.statistics` / `.seasonNumber`
  // inside type-guarded branches that never execute at runtime.
  const latestSeason = undefined as unknown as Season | undefined;

  return {
    manga,
    qualityProfile,
    latestSeason,
    isRefreshingManga,
    isSearchingManga,
  };
}

export default useMangaIndexItem;
