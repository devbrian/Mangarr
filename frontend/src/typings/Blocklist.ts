// Sonarr divergence: Phase 15 Plan 15-12 — Quality/Quality stripped per cascade absorption (Plan 15-03 deleted Quality cascade). `quality` typed as unknown so legacy TV-shape Blocklist rows compile; manga uses typings/MangaBlocklist instead and ignores this field. Phase 8 cleanup: collapse with MangaBlocklist.
import ModelBase from 'App/ModelBase';
import DownloadProtocol from 'DownloadClient/DownloadProtocol';
import Language from 'Language/Language';
import CustomFormat from 'typings/CustomFormat';

interface Blocklist extends ModelBase {
  languages: Language[];
  quality: unknown;
  customFormats: CustomFormat[];
  title: string;
  date?: string;
  protocol: DownloadProtocol;
  sourceTitle: string;
  seriesId?: number;
  indexer?: string;
  message?: string;
  source?: string;
}

export default Blocklist;
