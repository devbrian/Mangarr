// Phase 15 Plan 15-07 (Wave 3) cutover — root '/' flipped from SeriesIndex -> MangaIndex
// per Phase 7 D-09 cutover plan; TV stub routes (/series/:slug, /add/new, /add/import,
// /serieseditor, /seasonpass, TV /activity/*, TV /wanted/*, /settings/quality) deleted
// per Phase 14 Wave 3 row 7 cascade-pressure extension. Manga-rooted /manga/* siblings
// from Phase 7 Plans 07-04..07-10 are now the canonical paths. Legacy /series/:slug URLs
// hard-404 (per 15-CONTEXT discretion lean: no Sonarr-managing-manga users; no inbound
// bookmark base; redirect not warranted).
//
// GH #174 fix (debug session gh174-urlbase-redirect-spa-bug): the urlBase redirect Route
// is now declared ABOVE the unconditional MangaIndex Route. React Router v5 <Switch>
// is first-match-wins by child-declaration order; placing the redirect first guarantees
// that on a hosted-under-urlBase deployment (`window.Mangarr.urlBase` non-empty), the
// browser request for "/" picks the redirect before MangaIndex. The prior order (MangaIndex
// first) relied on the inner Switch wrapper prepending urlBase to MangaIndex's path so
// that "/" no longer matched it — empirically that path-mutation-driven ordering failed
// in the Phase 20 UrlBaseRedirectFixture, so the fix removes the dependency on subtle
// child-cloning / Children.map keying behaviour by making the redirect win declaratively.
import React from 'react';
import { Redirect, Route } from 'react-router-dom';
import MangaBlocklist from 'Activity/Blocklist/MangaBlocklist';
import MangaHistory from 'Activity/History/MangaHistory';
import MangaQueue from 'Activity/Queue/MangaQueue';
import AddNewManga from 'AddManga/AddNewManga/AddNewManga';
import ImportMangaPage from 'AddManga/ImportManga/ImportMangaPage';
import CalendarPage from 'Calendar/CalendarPage';
import NotFound from 'Components/NotFound';
import Switch from 'Components/Router/Switch';
import MangaDetailsPage from 'Manga/Details/MangaDetailsPage';
import MangaIndex from 'Manga/Index/MangaIndex';
import CustomFormatSettingsPage from 'Settings/CustomFormats/CustomFormatSettingsPage';
import DownloadClientSettings from 'Settings/DownloadClients/DownloadClientSettings';
import GeneralSettings from 'Settings/General/GeneralSettings';
import ImportListSettings from 'Settings/ImportLists/ImportListSettings';
import IndexerSettings from 'Settings/Indexers/IndexerSettings';
import MediaManagement from 'Settings/MediaManagement/MediaManagement';
import MetadataSettings from 'Settings/Metadata/MetadataSettings';
import MetadataSourceSettings from 'Settings/MetadataSource/MetadataSourceSettings';
import NotificationSettings from 'Settings/Notifications/NotificationSettings';
import CustomFormatProfileSettings from 'Settings/Profiles/CustomFormatProfile/CustomFormatProfileSettings';
import Profiles from 'Settings/Profiles/Profiles';
import Settings from 'Settings/Settings';
import TagSettings from 'Settings/Tags/TagSettings';
import UISettings from 'Settings/UI/UISettings';
import Backups from 'System/Backup/Backups';
import LogsTable from 'System/Events/LogsTable';
import Logs from 'System/Logs/Logs';
import Status from 'System/Status/Status';
import Tasks from 'System/Tasks/Tasks';
import Updates from 'System/Updates/Updates';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import MangaCutoffUnmet from 'Wanted/CutoffUnmet/MangaCutoffUnmet';
import MangaMissing from 'Wanted/Missing/MangaMissing';

function RedirectWithUrlBase() {
  return <Redirect to={getPathWithUrlBase('/')} />;
}

function AppRoutes() {
  return (
    <Switch>
      {/*
        Manga (Phase 15 Plan 15-07 cutover — root '/' flipped to MangaIndex per Phase 7
        D-09 + Phase 15 D-09 close-out. TV /series/:slug, /add/new, /add/import,
        /serieseditor, /seasonpass routes deleted. See .planning/phases/15-domain-rename-
        rebrand/15-07-PLAN.md.)

        GH #174: redirect Route declared BEFORE MangaIndex so first-match-wins in <Switch>
        picks the redirect when window.Mangarr.urlBase is non-empty (hosted-under-urlBase
        deployment). Without urlBase the conditional renders false and Switch falls through
        to MangaIndex.
      */}

      {window.Mangarr.urlBase && (
        <Route
          exact={true}
          path="/"
          // eslint-disable-next-line @typescript-eslint/ban-ts-comment
          // @ts-ignore
          addUrlBase={false}
          render={RedirectWithUrlBase}
        />
      )}

      <Route exact={true} path="/" component={MangaIndex} />

      {/*
        Phase 7 Plan 07-10 Rule 1 fix — `exact={true}` is REQUIRED to prevent
        prefix-matching `/manga/wanted/missing`, `/manga/activity/queue` etc.
        from rendering MangaDetailsPage with `titleSlug='wanted'` or
        `titleSlug='activity'`. React Router v5's <Switch> uses first-match-
        wins in declaration order, and a non-exact `:titleSlug` route compiles
        to a prefix-match regex that DOES match multi-segment URLs. Without
        `exact={true}`, the manga sub-routes registered later in this Switch
        (Plan 07-09 manga Activity routes; Plan 07-10 manga Wanted routes)
        would be unreachable. MangaDetailsPage has no nested routing so
        `exact={true}` is safe. See .planning/phases/07-api-v5-frontend-
        manga-shell/07-10-SUMMARY.md "Auto-fixed Issues" for the trace.
      */}
      <Route
        exact={true}
        path="/manga/:titleSlug"
        component={MangaDetailsPage}
      />

      <Route path="/add/manga" component={AddNewManga} />

      <Route path="/add/import" component={ImportMangaPage} />

      {/* Phase 25.1 Plan 25.1-02 — /add/import flipped from manual-import-as-page to
          Sonarr-canonical library-import flow (D-02). ImportMangaPage owns the
          nested <Switch> for /add/import (selector) vs /add/import/:rootFolderId
          (per-folder scan). Wanted/Missing modal path (InteractiveImportContent +
          InteractiveImportModal) UNTOUCHED per CONTEXT.md domain. */}

      {/*
        Calendar
      */}

      <Route path="/calendar" component={CalendarPage} />

      {/*
        Manga Activity (Phase 7 Plan 07-09 — additive per D-09 + D-10 + Lock #1.
        Phase 15 Plan 15-07: TV /activity/{queue,history,blocklist} routes DELETED
        per Phase 14 Wave 3 row 7 cascade-pressure extension; manga-rooted siblings
        below are now the canonical paths. See .planning/phases/07-api-v5-frontend-
        manga-shell/07-09-PLAN.md.)
      */}

      <Route path="/manga/activity/queue" component={MangaQueue} />

      <Route path="/manga/activity/history" component={MangaHistory} />

      <Route path="/manga/activity/blocklist" component={MangaBlocklist} />

      {/*
        Manga Wanted (Phase 7 Plan 07-10 + Phase 12 Plan 12-08 — additive per D-09 +
        D-10 + Lock #1. Phase 15 Plan 15-07: TV /wanted/{missing,cutoffunmet} routes
        DELETED per Phase 14 Wave 3 row 7 cascade-pressure extension; manga-rooted
        siblings below are now the canonical paths. See .planning/phases/07-api-v5-
        frontend-manga-shell/07-10-PLAN.md + .planning/phases/12-mock-contract-
        frontend-parity-audit/12-08-PLAN.md.)
      */}

      <Route path="/manga/wanted/missing" component={MangaMissing} />

      <Route path="/manga/wanted/cutoffunmet" component={MangaCutoffUnmet} />

      {/*
        Settings
      */}

      <Route exact={true} path="/settings" component={Settings} />

      <Route path="/settings/mediamanagement" component={MediaManagement} />

      <Route path="/settings/profiles" component={Profiles} />

      {/* Phase 7 D-05 — additive route for the new Custom Format Profiles page (Phase 5 Plan 05-03 entity).
          See Settings/Profiles/CustomFormatProfile/CLAUDE.md and 07-07-PLAN.md. */}
      <Route
        path="/settings/customformatprofiles"
        component={CustomFormatProfileSettings}
      />

      {/* Sonarr divergence: Phase 15 D-12 — /settings/quality route DELETED entirely;
          manga uses TranslationProfile + CustomFormatProfile per Phase 5. */}

      <Route
        path="/settings/customformats"
        component={CustomFormatSettingsPage}
      />

      <Route path="/settings/indexers" component={IndexerSettings} />

      <Route
        path="/settings/downloadclients"
        component={DownloadClientSettings}
      />

      <Route path="/settings/importlists" component={ImportListSettings} />

      <Route path="/settings/connect" component={NotificationSettings} />

      <Route path="/settings/metadata" component={MetadataSettings} />

      <Route
        path="/settings/metadatasource"
        component={MetadataSourceSettings}
      />

      <Route path="/settings/tags" component={TagSettings} />

      <Route path="/settings/general" component={GeneralSettings} />

      <Route path="/settings/ui" component={UISettings} />

      {/*
        System
      */}

      <Route path="/system/status" component={Status} />

      <Route path="/system/tasks" component={Tasks} />

      <Route path="/system/backup" component={Backups} />

      <Route path="/system/updates" component={Updates} />

      <Route path="/system/events" component={LogsTable} />

      <Route path="/system/logs/files" component={Logs} />

      {/*
        Not Found
      */}

      <Route path="*" component={NotFound} />
    </Switch>
  );
}

export default AppRoutes;
