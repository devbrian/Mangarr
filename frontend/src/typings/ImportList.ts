import { MangaMonitor } from 'Manga/Manga';
import Provider from './Provider';

// Sonarr divergence: Phase 15 Plan 15-12 — Series/Series imports stripped per
// cascade absorption (Plan 15-07 deleted Series subtree). The remaining TV-shape
// import-list fields stay so the verbatim-inherited Settings/ImportLists components
// compile; manga's import-list rows use MangaMonitor (Manga/Manga.ts). #356/#357:
// shouldMonitor retyped to the canonical 7-value MangaMonitor; the separate
// new-chapter axis was removed (derived from the Monitor choice server-side).
// Phase 8 cleanup: collapse with manga import-list resource.

type SeriesType = 'standard' | 'daily' | 'anime';

interface ImportList extends Provider {
  enable: boolean;
  enableAutomaticAdd: boolean;
  searchForMissingEpisodes: boolean;
  qualityProfileId: number;
  rootFolderPath: string;
  shouldMonitor: MangaMonitor;
  seriesType: SeriesType;
  seasonFolder: boolean;
  listType: string;
  listOrder: number;
  minRefreshInterval: string;
  name: string;
  tags: number[];
}

export default ImportList;
