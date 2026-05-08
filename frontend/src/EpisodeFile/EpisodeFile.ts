// Sonarr divergence: Phase 15 Plan 15-12 — STUB type for legacy consumers.
import ModelBase from 'App/ModelBase';
export interface EpisodeFile extends ModelBase {
  seriesId?: number;
  seasonNumber?: number;
  relativePath?: string;
  path?: string;
  size?: number;
  dateAdded?: string;
  customFormats?: unknown[];
  customFormatScore?: number;
  languages?: unknown[];
  quality?: unknown;
  qualityCutoffNotMet?: boolean;
  mediaInfo?: unknown;
  releaseGroup?: string;
  sceneName?: string;
  episodeIds?: number[];
  episodeNumbers?: number[];
}
export default EpisodeFile;
