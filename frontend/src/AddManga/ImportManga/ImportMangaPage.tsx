// Sonarr divergence: NEW per Phase 25.1 D-02 — see 25.1-SUMMARY.md.
// Role-match analog: frontend/src/AddSeries/ImportSeries/ImportSeriesPage.tsx
// (RR v6 syntax in upstream — `<Route element={...}>` + `index={true}`;
// this Mangarr port adapts to RR v5 per L-RR6 — `<Route component={...}>`
// + `exact={true}`).
//
// Manga sibling preserves: parent-Switch host pattern, sub-route discrimination
// between root-folder selector (exact /add/import) and per-folder scan
// (/add/import/:rootFolderId).
// Manga sibling diverges from ImportSeriesPage:
//   - RR v5 syntax instead of RR v6 (component=/exact= instead of
//     element=/index=).
//   - Uses Mangarr's custom Components/Router/Switch wrapper (PATTERNS
//     finding #4) so getPathWithUrlBase is applied to child Route path
//     props automatically — required for GH-#174 urlBase-prefixed
//     deployments.
//
// L-RR5-EXACT-ORDERING: the inner Switch's exact={true} on /add/import is
// load-bearing. WITHOUT it, RR v5's prefix-matching <Route> would shadow
// the /:rootFolderId child route at any nested path. The parent <Route>
// in AppRoutes.tsx MUST NOT carry `exact` — that would prevent
// /add/import/:rootFolderId from ever entering this page. The
// `ImportMangaPageRoutingFixture.rootFolderId_path_does_not_match_selector`
// fixture pins this contract.
//
// Phase 8 cleanup: collapse with ImportSeriesPage when AddSeries/ deletes.
import React from 'react';
import { Route } from 'react-router-dom';
import Switch from 'Components/Router/Switch';
import ImportManga from './ImportManga/ImportManga';
import ImportMangaSelectFolder from './ImportMangaSelectFolder/ImportMangaSelectFolder';

function ImportMangaPage() {
  return (
    <Switch>
      <Route
        exact={true}
        path="/add/import"
        component={ImportMangaSelectFolder}
      />
      <Route
        path="/add/import/:rootFolderId"
        component={ImportManga}
      />
    </Switch>
  );
}

export default ImportMangaPage;
