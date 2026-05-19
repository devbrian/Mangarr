// Sonarr divergence: NEW manga sibling per debug-add-import-ui-mismatch fix
// (2026-05-19). Role-match analog: frontend/src/Series/useExistingSeries.ts.
//
// Manga sibling preserves: shape (boolean return, useMemo gate, undefined
// id => false), data source (the canonical /manga library query cache via
// useManga()).
// Manga sibling diverges from useExistingSeries:
//   - Identifier is mangaDexId (string) instead of tvdbId (number). MangaDex
//     IDs are the closest 1:1-across-aggregator-sources analog (Phase 2
//     02-CONTEXT) — AniListId / MalId fall back if mangaDexId is absent.
//
// Used by ImportMangaRow + ImportMangaSelectManga to grey out + disable
// rows whose target manga is already in the library, mirroring Sonarr's
// "Existing" badge UX in the AddSeries/ImportSeries flow.
import { useMemo } from 'react';
import useManga from 'Manga/useManga';

function useExistingManga(mangaDexId: string | undefined) {
  const { data: manga } = useManga();

  return useMemo(() => {
    if (mangaDexId == null) {
      return false;
    }

    return manga.some((m) => m.mangaDexId === mangaDexId);
  }, [mangaDexId, manga]);
}

export default useExistingManga;
