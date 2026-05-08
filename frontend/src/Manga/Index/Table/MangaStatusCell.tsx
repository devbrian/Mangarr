import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import VirtualTableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons } from 'Helpers/Props';
import { MangaStatus } from 'Manga/Manga';
import { getSeriesStatusDetails } from "Series/SeriesStatus";
import { useToggleMangaMonitored } from 'Manga/useManga';
import translate from 'Utilities/String/translate';
import styles from './MangaStatusCell.css';

interface MangaStatusCellProps {
  className: string;
  mangaId: number;
  monitored: boolean;
  status: MangaStatus;
  isSelectMode: boolean;
  component?: React.ElementType;
}

function MangaStatusCell({
  className,
  mangaId,
  monitored,
  status,
  isSelectMode,
  component: Component = VirtualTableRowCell,
  ...otherProps
}: MangaStatusCellProps) {
  const statusDetails = getSeriesStatusDetails(status);
  const { toggleMangaMonitored, isTogglingMangaMonitored } =
    useToggleMangaMonitored(mangaId);

  const onMonitoredPress = useCallback(() => {
    toggleMangaMonitored({ monitored: !monitored });
  }, [monitored, toggleMangaMonitored]);

  return (
    <Component className={className} {...otherProps}>
      {isSelectMode ? (
        <MonitorToggleButton
          className={styles.statusIcon}
          monitored={monitored}
          isSaving={isTogglingMangaMonitored}
          onPress={onMonitoredPress}
        />
      ) : (
        <Icon
          className={styles.statusIcon}
          name={monitored ? icons.MONITORED : icons.UNMONITORED}
          title={
            monitored
              ? translate('SeriesIsMonitored')
              : translate('SeriesIsUnmonitored')
          }
        />
      )}

      <Icon
        className={styles.statusIcon}
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        name={statusDetails.icon as any}
        title={`${statusDetails.title}: ${statusDetails.message}`}
      />
    </Component>
  );
}

export default MangaStatusCell;
