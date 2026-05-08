import Provider from './Provider';

// Sonarr divergence: Phase 15 Plan 15-12 — Series/Series imports stripped per
// cascade absorption (Plan 15-07 deleted Series subtree). The TV-shape import-list
// fields stay so the verbatim-inherited Settings/ImportLists components compile;
// manga uses MangaMonitor / MangaType (Manga/Manga.ts) for its own import-list rows.
// Phase 8 cleanup: collapse with manga import-list resource.

type SeriesMonitor =
  | 'all'
  | 'future'
  | 'missing'
  | 'existing'
  | 'firstSeason'
  | 'lastSeason'
  | 'pilot'
  | 'recent'
  | 'monitorSpecials'
  | 'unmonitorSpecials'
  | 'none';

type MonitorNewItems = 'all' | 'none';

type SeriesType = 'standard' | 'daily' | 'anime';

interface ImportList extends Provider {
  enable: boolean;
  enableAutomaticAdd: boolean;
  searchForMissingEpisodes: boolean;
  qualityProfileId: number;
  rootFolderPath: string;
  shouldMonitor: SeriesMonitor;
  monitorNewItems: MonitorNewItems;
  seriesType: SeriesType;
  seasonFolder: boolean;
  listType: string;
  listOrder: number;
  minRefreshInterval: string;
  name: string;
  tags: number[];
}

export default ImportList;
