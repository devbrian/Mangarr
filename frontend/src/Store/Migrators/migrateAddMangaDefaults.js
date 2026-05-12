// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// 'Utilities/Series/monitorOptions' rewritten to 'Utilities/Manga/monitorOptions'
// peer (authored in this plan as a no-op empty array matching the Phase 15
// Plan 15-12 STUB shape). The persisted-state keys `addSeries.defaults.monitor`
// CANNOT change per D-12 lock — Redux migrator preserves legacy state-key
// names. Phase 17.3-02 already renamed the FILE migrateAddSeriesDefaults.js
// -> migrateAddMangaDefaults.js; only the monitor-options import is rewritten
// in this commit.
import { get } from 'lodash';
import monitorOptions from 'Utilities/Manga/monitorOptions';

export default function migrateAddMangaDefaults(persistedState) {
  const monitor = get(persistedState, 'addSeries.defaults.monitor');

  if (!monitor) {
    return;
  }

  if (!monitorOptions.find((option) => option.key === monitor)) {
    persistedState.addSeries.defaults.monitor = monitorOptions[0].key;
  }
}
