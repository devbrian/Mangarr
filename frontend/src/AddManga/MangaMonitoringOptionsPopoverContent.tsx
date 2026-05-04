// Sonarr divergence: NEW manga sibling per Phase 7 D-04 — see DIVERGENCE.md.
// Role-match analog: frontend/src/AddSeries/SeriesMonitoringOptionsPopoverContent.tsx.
//
// Manga sibling preserves: popover layout, monitor-label-with-description-row pattern.
// Manga sibling diverges from SeriesMonitoringOptions:
//   * 5 entries (Phase 6 D-03), not 11.
//   * No 'pilot' / 'firstSeason' / 'lastSeason' / 'recent' / 'monitorSpecials' /
//     'unmonitorSpecials' / 'existing' — these are TV-shape concepts that have
//     no manga equivalent.
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
