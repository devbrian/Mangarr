// Sonarr divergence: Phase 15 Plan 15-12 — re-export Manga as Series.
import Manga, {
  MangaMonitor,
  MangaType,
  MonitorNewItems as MNI,
  AlternateTitle as MAlternateTitle,
} from 'Manga/Manga';

export type SeriesMonitor = MangaMonitor;
export type SeriesType = MangaType;
export type MonitorNewItems = MNI;
export type AlternateTitle = MAlternateTitle;

interface Series extends Manga {}
export default Series;
