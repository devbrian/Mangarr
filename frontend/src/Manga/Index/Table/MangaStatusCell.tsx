// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 atomic delete fix-forward)
// — `getSeriesStatusDetails` from `Series/SeriesStatus` rewritten to
// `getMangaStatusDetails` from `Manga/MangaStatus` peer (authored alongside
// this rewrite). Returns manga-domain status copy + icon keyed by MangaStatus
// value instead of the deleted Phase 15 Plan 15-12 STUB's generic
// { title:'', message:'', icon:'rss' } no-op shape.
import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import VirtualTableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons } from 'Helpers/Props';
import { MangaStatus } from 'Manga/Manga';
import { getMangaStatusDetails } from 'Manga/MangaStatus';
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
  const statusDetails = getMangaStatusDetails(status);
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
