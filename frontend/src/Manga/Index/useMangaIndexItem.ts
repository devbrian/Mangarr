// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Index/useSeriesIndexItem.ts.
//
// Manga sibling preserves: composes useSingleManga + useMangaQualityProfile
// + useCommandExecuting for refresh/search executing flags.
//
// Phase 17.3 D-13/D-14 (2026-05-11): dropped the `latestSeason` return-shape
// carry-over + the orphan `Season` import (manga has no seasons per
// DOMAIN-02 / Plan 07-04 Lock #10; the Manga.ts D-13 trim deleted the
// Season interface, breaking the import). The Wave 4 component forks
// (MangaIndexRow Table view per Plan 17.3-08, MangaIndexOverview per
// 17.3-09, MangaIndexPoster per 17.3-10) dropped every consumer-side
// `latestSeason` destructure too — no callsite still reads it.
//
// Phase 8 cleanup: collapse with useSeriesIndexItem when Tv/ deletes.
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting } from 'Commands/useCommands';
import { useSingleManga } from 'Manga/useManga';
import useMangaQualityProfile from 'Manga/useMangaQualityProfile';

export function useMangaIndexItem(mangaId: number) {
  const manga = useSingleManga(mangaId);
  const qualityProfile = useMangaQualityProfile(manga);

  const isRefreshingManga = useCommandExecuting(CommandNames.RefreshManga, {
    mangaIds: [mangaId],
  });

  // The constraint body MUST mirror the dispatched MangaSearch payload shape
  // (`mangaIds: [mangaId]`) — useCommand() matches constraints against the live
  // command's `body`, and MangaSearchCommand binds `MangaIds: List<int>`
  // (plural). A singular `{ mangaId }` constraint never matches the running
  // command (`command.body.mangaId` is undefined), so isSearchingManga stays
  // false forever and the search SpinnerIconButton never spins — no click
  // feedback. Mirrors isRefreshingManga above + MangaDetails.isSearching.
  const isSearchingManga = useCommandExecuting(CommandNames.MangaSearch, {
    mangaIds: [mangaId],
  });

  return {
    manga,
    qualityProfile,
    isRefreshingManga,
    isSearchingManga,
  };
}

export default useMangaIndexItem;
