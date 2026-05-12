// Sonarr divergence: Phase 17.3 Plan 17.3-13 (D-09 stub-importer cascade) —
// Episode/Episode rewritten to Chapter/Chapter peer; Series/Series rewritten to
// Manga/Manga peer. The TV-shape field NAMES (seriesId, episodeIds, episodeFileId,
// seasonNumber, episodes, series) are preserved on the InteractiveImport DTO + the
// command-options surface because the backend response (TV-only InteractiveImport
// flow, gated for v1 per Phase 12 Plan 12-11 LOCK guard) still emits them; only
// the typescript types referenced by those fields are migrated to manga peers so
// the stub-importer cascade closes. Phase 8+ cleanup: drop the TV-shape field
// names from the DTO once the manga-side InteractiveImport discriminator
// extension ships (Plan 12-98 v1.1+ roadmap entry).
import ModelBase from 'App/ModelBase';
import Chapter from 'Chapter/Chapter';
import ReleaseType from 'InteractiveImport/ReleaseType';
import Language from 'Language/Language';
import Manga from 'Manga/Manga';
import { QualityModel } from 'Quality/Quality';
import CustomFormat from 'typings/CustomFormat';
import Rejection from 'typings/Rejection';

export interface InteractiveImportCommandOptions {
  path: string;
  folderName: string;
  seriesId: number;
  episodeIds: number[];
  releaseGroup?: string;
  quality: QualityModel;
  languages: Language[];
  indexerFlags: number;
  releaseType: ReleaseType;
  downloadId?: string;
  episodeFileId?: number;
}

interface InteractiveImport extends ModelBase {
  path: string;
  relativePath: string;
  folderName: string;
  name: string;
  size: number;
  releaseGroup: string;
  quality: QualityModel;
  languages: Language[];
  series?: Manga;
  seasonNumber: number;
  episodes: Chapter[];
  qualityWeight: number;
  customFormats: CustomFormat[];
  indexerFlags: number;
  releaseType: ReleaseType;
  rejections: Rejection[];
  episodeFileId?: number;
  downloadId?: string;
}

export default InteractiveImport;
