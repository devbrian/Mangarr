// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade —
// deferred-from-Plan-17.3-12 ParseModel.ts cross-tree consumer) —
// Episode/Episode rewritten to Chapter/Chapter peer; Series/Series rewritten
// to Manga/Manga peer. The TV-shape field names on the inherited ParseModel
// + ParsedEpisodeInfo DTOs are preserved at runtime (backend Parse
// controller still emits the TV shape for the cross-domain parse endpoint
// surface); only the typescript types referenced by these fields are
// migrated to manga peers so the stub-importer cascade closes.
import ModelBase from 'App/ModelBase';
import Chapter from 'Chapter/Chapter';
import Language from 'Language/Language';
import Manga from 'Manga/Manga';
import { QualityModel } from 'Quality/Quality';
import CustomFormat from 'typings/CustomFormat';

export interface SeriesTitleInfo {
  title: string;
  titleWithoutYear: string;
  year: number;
  allTitles: string[];
}

export interface ParsedEpisodeInfo {
  releaseTitle: string;
  seriesTitle: string;
  seriesTitleInfo: SeriesTitleInfo;
  quality: QualityModel;
  seasonNumber: number;
  episodeNumbers: number[];
  absoluteEpisodeNumbers: number[];
  specialAbsoluteEpisodeNumbers: number[];
  languages: Language[];
  fullSeason: boolean;
  isPartialSeason: boolean;
  isMultiSeason: boolean;
  isSeasonExtra: boolean;
  special: boolean;
  releaseHash: string;
  seasonPart: number;
  releaseGroup?: string;
  releaseTokens: string;
  airDate?: string;
  isDaily: boolean;
  isAbsoluteNumbering: boolean;
  isPossibleSpecialEpisode: boolean;
  isPossibleSceneSeasonSpecial: boolean;
}

export interface ParseModel extends ModelBase {
  title: string;
  parsedEpisodeInfo: ParsedEpisodeInfo;
  series?: Manga;
  episodes: Chapter[];
  languages?: Language[];
  customFormats?: CustomFormat[];
  customFormatScore?: number;
}
