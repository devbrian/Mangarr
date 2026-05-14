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
//   * Routes via /manga/:titleSlug (D-09 — additive; manga details page).
//   * Redirect-on-vanish target is `/` (the manga library index after the
//     Phase 15 Plan 15-07 cutover flipped root from SeriesIndex to
//     MangaIndex). Bare `/manga` is NOT registered in AppRoutes.tsx and
//     would 404; only `/manga/:titleSlug` is registered.
//
// Phase 8 cleanup: when Series/Details/ deletes, this becomes the canonical
// detail page.
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

  const mangaIndex = allManga.findIndex(
    (manga) => manga.titleSlug === titleSlug
  );
  const previousIndex = usePrevious(mangaIndex);

  useEffect(() => {
    if (
      mangaIndex === -1 &&
      previousIndex !== -1 &&
      previousIndex !== undefined
    ) {
      // Issue #34 fix: redirect to `/` (the manga library index after the
      // Phase 15 Plan 15-07 cutover flipped root from SeriesIndex to
      // MangaIndex). Bare `/manga` is unregistered in AppRoutes.tsx and
      // would render the NotFound 404 illustration.
      history.push(`${window.Mangarr.urlBase}/`);
    }
  }, [mangaIndex, previousIndex, history]);

  if (mangaIndex === -1) {
    return <NotFound message={translate('MangaCannotBeFound')} />;
  }

  return <MangaDetails mangaId={allManga[mangaIndex].id} />;
}

export default MangaDetailsPage;
