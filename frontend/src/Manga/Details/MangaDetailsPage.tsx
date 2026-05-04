// Sonarr divergence: NEW manga sibling per Phase 7 D-01 / D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/SeriesDetailsPage.tsx
// (verbatim port — title-slug routing + NotFound fallback + history.push
// redirect when the manga becomes unfindable mid-page).
//
// Manga sibling preserves: useParams<{ titleSlug }>, NotFound fallback,
// usePrevious-driven redirect when the manga is deleted out from under the
// page, useEffect dependency array shape.
// Manga sibling diverges from SeriesDetailsPage:
//   * useManga (instead of useSeries) — list-shaped /api/v5/manga.
//   * Routes via /manga/:titleSlug (D-09 — additive; / stays on TV until
//     Phase 8 cutover).
//   * Redirect target is /manga (not /), keeping the user on the manga
//     library page when the manga vanishes.
//
// Phase 8 cleanup: when Series/Details/ deletes, this becomes the canonical
// detail page. Phase 8 also swaps `/manga` → `/` per D-09 cutover.
import React, { useEffect } from 'react';
import { useHistory, useParams } from 'react-router';
import NotFound from 'Components/NotFound';
import usePrevious from 'Helpers/Hooks/usePrevious';
import useManga from 'Manga/useManga';
import translate from 'Utilities/String/translate';
import MangaDetails from './MangaDetails';

function MangaDetailsPage() {
  const { data: allManga } = useManga();
  const { titleSlug } = useParams<{ titleSlug: string }>();
  const history = useHistory();

  const mangaIndex = allManga.findIndex((manga) => manga.titleSlug === titleSlug);
  const previousIndex = usePrevious(mangaIndex);

  useEffect(() => {
    if (
      mangaIndex === -1 &&
      previousIndex !== -1 &&
      previousIndex !== undefined
    ) {
      // Phase 8 cutover swaps /manga → /; until then the manga library page
      // stays at /manga.
      history.push(`${window.Sonarr.urlBase}/manga`);
    }
  }, [mangaIndex, previousIndex, history]);

  if (mangaIndex === -1) {
    return <NotFound message={translate('MangaCannotBeFound')} />;
  }

  return <MangaDetails mangaId={allManga[mangaIndex].id} />;
}

export default MangaDetailsPage;
