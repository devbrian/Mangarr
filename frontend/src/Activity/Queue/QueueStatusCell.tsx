import React from 'react';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import {
  QueueTrackedDownloadState,
  QueueTrackedDownloadStatus,
  StatusMessage,
} from 'typings/Queue';
import QueueStatus from './QueueStatus';
import styles from './QueueStatusCell.css';

interface QueueStatusCellProps {
  sourceTitle: string;
  status: string;
  trackedDownloadStatus?: QueueTrackedDownloadStatus;
  trackedDownloadState?: QueueTrackedDownloadState;
  statusMessages?: StatusMessage[];
  errorMessage?: string;
  // Phase 18 Plan-05: optional data-testid plumbing for Playwright row-cell state
  // assertions (per .planning/phases/18-.../inventory/data-testid-spec.md). Stays
  // optional so existing callers don't change shape; QueueRow.tsx supplies the
  // row-scoped testid.
  'data-testid'?: string;
}

function QueueStatusCell(props: QueueStatusCellProps) {
  const {
    sourceTitle,
    status,
    trackedDownloadStatus = 'ok',
    trackedDownloadState = 'downloading',
    statusMessages,
    errorMessage,
    'data-testid': dataTestId,
  } = props;

  return (
    <TableRowCell className={styles.status} data-testid={dataTestId}>
      <QueueStatus
        sourceTitle={sourceTitle}
        status={status}
        trackedDownloadStatus={trackedDownloadStatus}
        trackedDownloadState={trackedDownloadState}
        statusMessages={statusMessages}
        errorMessage={errorMessage}
        position="right"
      />
    </TableRowCell>
  );
}

export default QueueStatusCell;
