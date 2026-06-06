import React from 'react';
import Icon from 'Components/Icon';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons, kinds } from 'Helpers/Props';
import {
  EpisodeFileDeletedHistory,
  GrabbedHistoryData,
  HistoryData,
  HistoryEventType,
} from 'typings/History';
import translate from 'Utilities/String/translate';
import styles from './HistoryEventTypeCell.css';

function getIconName(eventType: HistoryEventType, data: HistoryData) {
  switch (eventType) {
    case 'grabbed':
      return icons.DOWNLOADING;
    case 'seriesFolderImported':
      return icons.DRIVE;
    case 'downloadFolderImported':
      return icons.DOWNLOADED;
    // Manga side emits `imported` / `importFailed` (Phase 6 ChapterHistoryEventType)
    // instead of TV's `downloadFolderImported` (the TV side has both an episode-folder
    // and a download-folder import path; manga only has the download-folder one).
    case 'imported':
      return icons.DOWNLOADED;
    case 'importFailed':
      return icons.DOWNLOADING;
    case 'downloadFailed':
      return icons.DOWNLOADING;
    case 'episodeFileDeleted':
      return (data as EpisodeFileDeletedHistory).reason === 'MissingFromDisk'
        ? icons.FILE_MISSING
        : icons.DELETE;
    case 'episodeFileRenamed':
      return icons.ORGANIZE;
    // `ignored` is the manga peer of `downloadIgnored` — completed but deliberately not imported.
    case 'downloadIgnored':
    case 'ignored':
      return icons.IGNORE;
    default:
      return icons.UNKNOWN;
  }
}

function getIconKind(eventType: HistoryEventType) {
  switch (eventType) {
    case 'downloadFailed':
    case 'importFailed':
      return kinds.DANGER;
    default:
      return kinds.DEFAULT;
  }
}

function getTooltip(eventType: HistoryEventType, data: HistoryData) {
  switch (eventType) {
    case 'grabbed':
      return translate('EpisodeGrabbedTooltip', {
        indexer: (data as GrabbedHistoryData).indexer,
        downloadClient: (data as GrabbedHistoryData).downloadClient,
      });
    case 'seriesFolderImported':
      return translate('SeriesFolderImportedTooltip');
    case 'downloadFolderImported':
      return translate('EpisodeImportedTooltip');
    case 'imported':
      return translate('EpisodeImportedTooltip');
    case 'importFailed':
      return translate('DownloadFailedChapterTooltip');
    case 'downloadFailed':
      return translate('DownloadFailedChapterTooltip');
    case 'episodeFileDeleted':
      return (data as EpisodeFileDeletedHistory).reason === 'MissingFromDisk'
        ? translate('EpisodeFileMissingTooltip')
        : translate('EpisodeFileDeletedTooltip');
    case 'episodeFileRenamed':
      return translate('EpisodeFileRenamedTooltip');
    case 'downloadIgnored':
    case 'ignored':
      return translate('DownloadIgnoredChapterTooltip');
    default:
      return translate('UnknownEventTooltip');
  }
}

interface HistoryEventTypeCellProps {
  eventType: HistoryEventType;
  data: HistoryData;
  // Phase 18 Plan-05: optional data-testid for row-cell state assertions
  // (feedback_verify_ui_state_not_just_rendering.md — the decision/event-type
  // cell is the one that hides silent rejections; Playwright fixtures pull
  // the icon's `name` attribute or the cell's title text to verify state).
  'data-testid'?: string;
}

function HistoryEventTypeCell({
  eventType,
  data,
  'data-testid': dataTestId,
}: HistoryEventTypeCellProps) {
  const iconName = getIconName(eventType, data);
  const iconKind = getIconKind(eventType);
  const tooltip = getTooltip(eventType, data);

  return (
    <TableRowCell
      className={styles.cell}
      title={tooltip}
      data-testid={dataTestId}
      data-event-type={eventType}
    >
      <Icon name={iconName} kind={iconKind} />
    </TableRowCell>
  );
}

export default HistoryEventTypeCell;
