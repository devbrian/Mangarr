// Sonarr divergence: NEW manga sibling per Phase 7 D-04 — see DIVERGENCE.md.
// Role-match analog: frontend/src/AddSeries/SeriesMonitoringOptionsPopoverContent.tsx.
//
// Manga sibling preserves: popover layout, monitor-label-with-description-row pattern.
// Manga sibling diverges from SeriesMonitoringOptions:
//   * 7 entries (#357 D-2, supersedes the Phase 6 D-03 5-value lock), not 11.
//   * No 'pilot' / 'firstSeason' / 'lastSeason' / 'recent' / 'monitorSpecials' /
//     'unmonitorSpecials' — these are TV-shape concepts that have no manga equivalent.
//     ('existing' + 'first' DO have manga peers per #357 D-3.)
//   * Replaces 'Episodes' with 'Chapters' in copy.
//
// Phase 8 cleanup: collapse with SeriesMonitoringOptionsPopoverContent when
// AddSeries/ deletes.
import React from 'react';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import translate from 'Utilities/String/translate';

function MangaMonitoringOptionsPopoverContent() {
  return (
    <DescriptionList>
      <DescriptionListItem
        title={translate('MonitorAllChapters')}
        data={translate('MonitorAllChaptersDescription')}
      />

      <DescriptionListItem
        title={translate('MonitorFutureChapters')}
        data={translate('MonitorFutureChaptersDescription')}
      />

      <DescriptionListItem
        title={translate('MonitorMissingChapters')}
        data={translate('MonitorMissingChaptersDescription')}
      />

      <DescriptionListItem
        title={translate('MonitorExistingChapters')}
        data={translate('MonitorExistingChaptersDescription')}
      />

      <DescriptionListItem
        title={translate('MonitorFirstChapter')}
        data={translate('MonitorFirstChapterDescription')}
      />

      <DescriptionListItem
        title={translate('MonitorLatestChapter')}
        data={translate('MonitorLatestChapterDescription')}
      />

      <DescriptionListItem
        title={translate('MonitorNone')}
        data={translate('MonitorNoneDescription')}
      />
    </DescriptionList>
  );
}

export default MangaMonitoringOptionsPopoverContent;
