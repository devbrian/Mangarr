// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// authored as the manga-shape peer of the deleted Utilities/Series/monitorOptions
// stub. Originally a no-op empty array per Phase 15 Plan 15-12 STUB shape; the
// stub-comment flagged "v1.x cleanup ticket: populate with translated
// MangaMonitor labels keyed off Manga.ts MangaMonitor type" — that ticket was
// filed as issue #209 and is closed by populating the array below. #357 extended
// it to 7 values (added existing + first) when MangaMonitor unified to 7 values.
//
// Shape mirrors Sonarr's Utilities/Series/monitorOptions.ts verbatim (deferred
// translate() via get value() getter — language changes take effect without
// re-importing the module). Keys are typed as MangaMonitor (not loose string)
// so TypeScript catches drift if the literal extends.
import { MangaMonitor } from 'Manga/Manga';
import translate from 'Utilities/String/translate';

interface MonitorOption {
  key: MangaMonitor;
  value: string;
}

const monitorOptions: MonitorOption[] = [
  {
    key: 'all',
    get value() {
      return translate('MonitorAllChapters');
    },
  },
  {
    key: 'future',
    get value() {
      return translate('MonitorFutureChapters');
    },
  },
  {
    key: 'missing',
    get value() {
      return translate('MonitorMissingChapters');
    },
  },
  {
    key: 'existing',
    get value() {
      return translate('MonitorExistingChapters');
    },
  },
  {
    key: 'first',
    get value() {
      return translate('MonitorFirstChapter');
    },
  },
  {
    key: 'latest',
    get value() {
      return translate('MonitorLatestChapter');
    },
  },
  {
    key: 'none',
    get value() {
      return translate('MonitorNone');
    },
  },
];

export default monitorOptions;
