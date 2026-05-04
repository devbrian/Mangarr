import React from 'react';
import { Redirect, Route } from 'react-router-dom';
import Blocklist from 'Activity/Blocklist/Blocklist';
import History from 'Activity/History/History';
import Queue from 'Activity/Queue/Queue';
import AddNewManga from 'AddManga/AddNewManga/AddNewManga';
import AddNewSeries from 'AddSeries/AddNewSeries/AddNewSeries';
import ImportSeriesPage from 'AddSeries/ImportSeries/ImportSeriesPage';
import CalendarPage from 'Calendar/CalendarPage';
import NotFound from 'Components/NotFound';
import Switch from 'Components/Router/Switch';
import MangaDetailsPage from 'Manga/Details/MangaDetailsPage';
import MangaIndex from 'Manga/Index/MangaIndex';
import SeriesDetailsPage from 'Series/Details/SeriesDetailsPage';
import SeriesIndex from 'Series/Index/SeriesIndex';
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
import Quality from 'Settings/Quality/Quality';
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
import CutoffUnmet from 'Wanted/CutoffUnmet/CutoffUnmet';
import Missing from 'Wanted/Missing/Missing';

function RedirectWithUrlBase() {
  return <Redirect to={getPathWithUrlBase('/')} />;
}

function AppRoutes() {
  return (
    <Switch>
      {/*
        Series
      */}

      <Route exact={true} path="/" component={SeriesIndex} />

      {window.Sonarr.urlBase && (
        <Route
          exact={true}
          path="/"
          // eslint-disable-next-line @typescript-eslint/ban-ts-comment
          // @ts-ignore
          addUrlBase={false}
          render={RedirectWithUrlBase}
        />
      )}

      <Route path="/add/new" component={AddNewSeries} />

      <Route path="/add/import" component={ImportSeriesPage} />

      <Route path="/serieseditor" exact={true} render={RedirectWithUrlBase} />

      <Route path="/seasonpass" exact={true} render={RedirectWithUrlBase} />

      <Route path="/series/:titleSlug" component={SeriesDetailsPage} />

      {/*
        Manga (Phase 7 Plans 07-04 + 07-05 + 07-06 — additive per D-09; /
        stays on TV until Phase 8 cutover. See .planning/phases/07-api-v5-
        frontend-manga-shell/07-04-PLAN.md, 07-05-PLAN.md, 07-06-PLAN.md.)
      */}

      <Route exact={true} path="/manga" component={MangaIndex} />

      <Route path="/manga/:titleSlug" component={MangaDetailsPage} />

      <Route path="/add/manga" component={AddNewManga} />

      {/*
        Calendar
      */}

      <Route path="/calendar" component={CalendarPage} />

      {/*
        Activity
      */}

      <Route path="/activity/history" component={History} />

      <Route path="/activity/queue" component={Queue} />

      <Route path="/activity/blocklist" component={Blocklist} />

      {/*
        Wanted
      */}

      <Route path="/wanted/missing" component={Missing} />

      <Route path="/wanted/cutoffunmet" component={CutoffUnmet} />

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

      <Route path="/settings/quality" component={Quality} />

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
