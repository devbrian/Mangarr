import React, { useMemo } from 'react';
import { IconName } from 'Components/Icon';
import { icons } from 'Helpers/Props';
import { QualityProfileModel } from 'Settings/Profiles/Quality/useQualityProfiles';
import {
  UiSettingsModel,
  useUiSettingsValues,
} from 'Settings/UI/useUiSettings';
import dimensions from 'Styles/Variables/dimensions';
import formatDateTime from 'Utilities/Date/formatDateTime';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import MangaIndexOverviewInfoRow from './MangaIndexOverviewInfoRow';
import styles from './MangaIndexOverviewInfo.css';

interface RowProps {
  name: string;
  showProp: string;
  valueProp: string;
}

interface RowInfoProps {
  title: string;
  iconName: IconName;
  label: string;
}

interface MangaIndexOverviewInfoProps {
  height: number;
  showMonitored: boolean;
  showQualityProfile: boolean;
  showAdded: boolean;
  showPath: boolean;
  showSizeOnDisk: boolean;
  // Sonarr-shape carry-over: showNetwork / showPreviousAiring / showSeasonCount
  // dropped per Phase 17.3 D-14 (Overview-view fork). The Options modal still
  // surfaces these toggles in `overviewOptions` and they spread in via
  // `{...overviewOptions}` from the parent — they're accepted but ignored
  // because the corresponding info-row branches have been removed (no airing,
  // no network, no seasons in manga domain per DOMAIN-02).
  showNetwork?: boolean;
  showPreviousAiring?: boolean;
  showSeasonCount?: boolean;
  monitored: boolean;
  qualityProfile?: QualityProfileModel;
  added?: string;
  path: string;
  sizeOnDisk?: number;
  sortKey: string;
}

const infoRowHeight = parseInt(dimensions.seriesIndexOverviewInfoRowHeight);

// Phase 17.3 D-14: 'network' / 'previousAiring' / 'seasonCount' rows dropped
// (manga has no airing, no network, no seasons per DOMAIN-02). The
// corresponding `name === '...'` branches in getInfoRowProps were dropped in
// the same edit. The Options-modal toggles (showNetwork/showPreviousAiring/
// showSeasonCount) are i18n-sweep territory for Plan 17.3-13.
const rows = [
  {
    name: 'monitored',
    showProp: 'showMonitored',
    valueProp: 'monitored',
  },
  {
    name: 'qualityProfileId',
    showProp: 'showQualityProfile',
    valueProp: 'qualityProfile',
  },
  {
    name: 'added',
    showProp: 'showAdded',
    valueProp: 'added',
  },
  {
    name: 'path',
    showProp: 'showPath',
    valueProp: 'path',
  },
  {
    name: 'sizeOnDisk',
    showProp: 'showSizeOnDisk',
    valueProp: 'sizeOnDisk',
  },
];

function getInfoRowProps(
  row: RowProps,
  props: MangaIndexOverviewInfoProps,
  uiSettings: UiSettingsModel
): RowInfoProps | null {
  const { name } = row;

  if (name === 'monitored') {
    const monitoredText = props.monitored
      ? translate('Monitored')
      : translate('Unmonitored');

    return {
      title: monitoredText,
      iconName: props.monitored ? icons.MONITORED : icons.UNMONITORED,
      label: monitoredText,
    };
  }

  // Phase 17.3 D-14: name === 'network' branch dropped (no network in manga
  // domain per DOMAIN-02; Manga.ts trim removed props.network).

  if (name === 'qualityProfileId' && !!props.qualityProfile?.name) {
    return {
      title: translate('QualityProfile'),
      iconName: icons.PROFILE,
      label: props.qualityProfile.name,
    };
  }

  // Phase 17.3 D-14: name === 'previousAiring' branch dropped (no airing
  // concept in manga domain; Manga.ts trim removed props.previousAiring).

  if (name === 'added') {
    const added = props.added;
    const { showRelativeDates, shortDateFormat, longDateFormat, timeFormat } =
      uiSettings;

    return {
      title: translate('AddedDate', {
        date: formatDateTime(added, longDateFormat, timeFormat),
      }),
      iconName: icons.ADD,
      label:
        getRelativeDate({
          date: added,
          shortDateFormat,
          showRelativeDates,
          timeFormat,
          timeForToday: true,
        }) ?? '',
    };
  }

  // Phase 17.3 D-14: name === 'seasonCount' branch dropped (manga has no
  // seasons per DOMAIN-02; Statistics trim removed seasonCount).

  if (name === 'path') {
    return {
      title: translate('Path'),
      iconName: icons.FOLDER,
      label: props.path,
    };
  }

  if (name === 'sizeOnDisk') {
    const { sizeOnDisk = 0 } = props;

    return {
      title: translate('SizeOnDisk'),
      iconName: icons.DRIVE,
      label: formatBytes(sizeOnDisk),
    };
  }

  return null;
}

function MangaIndexOverviewInfo(props: MangaIndexOverviewInfoProps) {
  const { height } = props;

  // Phase 17.3 D-14: top-of-render nextAiring block dropped (no airing concept
  // in manga domain; Manga.ts trim removed props.nextAiring). The remaining
  // date-row branches (added) read their own uiSettings inside getInfoRowProps.
  const uiSettings = useUiSettingsValues();

  let shownRows = 1;
  const maxRows = Math.floor(height / (infoRowHeight + 4));

  const rowInfo = useMemo(() => {
    return rows.map((row) => {
      const { name, showProp, valueProp } = row;

      const isVisible =
        // eslint-disable-next-line @typescript-eslint/ban-ts-comment
        // @ts-ignore ts(7053)
        props[valueProp] != null && (props[showProp] || props.sortKey === name);

      return {
        ...row,
        isVisible,
      };
    });
  }, [props]);

  return (
    <div className={styles.infos}>
      {rowInfo.map((row) => {
        if (!row.isVisible) {
          return null;
        }

        if (shownRows >= maxRows) {
          return null;
        }

        shownRows++;

        const infoRowProps = getInfoRowProps(row, props, uiSettings);

        if (infoRowProps == null) {
          return null;
        }

        return <MangaIndexOverviewInfoRow key={row.name} {...infoRowProps} />;
      })}
    </div>
  );
}

export default MangaIndexOverviewInfo;
