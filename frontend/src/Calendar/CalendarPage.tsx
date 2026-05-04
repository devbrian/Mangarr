// Sonarr divergence: per Phase 7 D-discretion + Lock #15 — Calendar v2 placeholder banner — see DIVERGENCE.md.
// PROJECT.md Out-of-Scope: Calendar implementation deferred to v2. Manga release schedules are
// not deterministic in v1 (no aggregator timetables wired), so a banner above the calendar grid
// signals "feature shipping in v2" universally — TV calendar grid still renders below for users
// with TV series. UI-SPEC \xa7Empty states locks the copy: heading 'Calendar is coming in v2',
// body 'For now, monitor your manga and check Wanted to see what's missing.' i18n keys
// CalendarComingInV2 / CalendarComingInV2Hint land in Plan 07-11 en.json; until then translate()
// falls back to the key string at runtime — no crash.
import React, {
  PropsWithChildren,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from 'react';
import QueueDetailsProvider from 'Activity/Queue/Details/QueueDetailsProvider';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import FilterMenu from 'Components/Menu/FilterMenu';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import Episode from 'Episode/Episode';
import EpisodeFileProvider from 'EpisodeFile/EpisodeFileProvider';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import useMeasure from 'Helpers/Hooks/useMeasure';
import { align, icons, kinds } from 'Helpers/Props';
import NoSeries from 'Series/NoSeries';
import { useHasSeries } from 'Series/useSeries';
import selectUniqueIds from 'Utilities/Object/selectUniqueIds';
import translate from 'Utilities/String/translate';
import Calendar from './Calendar';
import CalendarFilterModal from './CalendarFilterModal';
import CalendarMissingEpisodeSearchButton from './CalendarMissingEpisodeSearchButton';
import { setCalendarOption, useCalendarOption } from './calendarOptionsStore';
import CalendarLinkModal from './iCal/CalendarLinkModal';
import Legend from './Legend/Legend';
import CalendarOptionsModal from './Options/CalendarOptionsModal';
import useCalendar, {
  FILTERS,
  setCalendarDayCount,
  useCalendarPage,
} from './useCalendar';
import styles from './CalendarPage.css';

const MINIMUM_DAY_WIDTH = 120;

function CalendarPage() {
  const executeCommand = useExecuteCommand();

  const selectedFilterKey = useCalendarOption('selectedFilterKey');
  const { data } = useCalendar();

  useCalendarPage();

  const isRssSyncExecuting = useCommandExecuting(CommandNames.RssSync);
  const customFilters = useCustomFiltersList('calendar');
  const hasSeries = useHasSeries();

  const [pageContentRef, { width }] = useMeasure();
  const [isCalendarLinkModalOpen, setIsCalendarLinkModalOpen] = useState(false);
  const [isOptionsModalOpen, setIsOptionsModalOpen] = useState(false);

  const isMeasured = width > 0;
  const PageComponent = hasSeries ? Calendar : NoSeries;

  const handleGetCalendarLinkPress = useCallback(() => {
    setIsCalendarLinkModalOpen(true);
  }, []);

  const handleGetCalendarLinkModalClose = useCallback(() => {
    setIsCalendarLinkModalOpen(false);
  }, []);

  const handleOptionsPress = useCallback(() => {
    setIsOptionsModalOpen(true);
  }, []);

  const handleOptionsModalClose = useCallback(() => {
    setIsOptionsModalOpen(false);
  }, []);

  const handleRssSyncPress = useCallback(() => {
    executeCommand({
      name: CommandNames.RssSync,
    });
  }, [executeCommand]);

  const handleFilterSelect = useCallback((key: string | number) => {
    setCalendarOption('selectedFilterKey', key);
  }, []);

  const episodeIds = useMemo(() => {
    return selectUniqueIds<Episode, number>(data, 'id');
  }, [data]);

  const episodeFileIds = useMemo(() => {
    return selectUniqueIds<Episode, number>(data, 'episodeFileId');
  }, [data]);

  useEffect(() => {
    if (width === 0) {
      return;
    }

    const dayCount = Math.max(
      3,
      Math.min(7, Math.floor(width / MINIMUM_DAY_WIDTH))
    );

    setCalendarDayCount(dayCount);
  }, [width]);

  return (
    <CalendarPageProvider
      episodeIds={episodeIds}
      episodeFileIds={episodeFileIds}
    >
      <PageContent title={translate('Calendar')}>
        <PageToolbar>
          <PageToolbarSection>
            <PageToolbarButton
              label={translate('ICalLink')}
              iconName={icons.CALENDAR}
              onPress={handleGetCalendarLinkPress}
            />

            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('RssSync')}
              iconName={icons.RSS}
              isSpinning={isRssSyncExecuting}
              onPress={handleRssSyncPress}
            />

            <CalendarMissingEpisodeSearchButton />
          </PageToolbarSection>

          <PageToolbarSection alignContent={align.RIGHT}>
            <PageToolbarButton
              label={translate('Options')}
              iconName={icons.POSTER}
              onPress={handleOptionsPress}
            />

            <FilterMenu
              alignMenu={align.RIGHT}
              isDisabled={!hasSeries}
              selectedFilterKey={selectedFilterKey}
              filters={FILTERS}
              customFilters={customFilters}
              filterModalConnectorComponent={CalendarFilterModal}
              onFilterSelect={handleFilterSelect}
            />
          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody
          ref={pageContentRef}
          className={styles.calendarPageBody}
          innerClassName={styles.calendarInnerPageBody}
        >
          {/*
            Phase 7 Plan 07-10 — v2 placeholder banner per UI-06 + Lock #15 +
            UI-SPEC \xa7Calendar / \xa7Empty states. Calendar implementation is
            deferred to v2 (PROJECT.md Out-of-Scope). Banner renders universally
            so users on the TV-side still get a heads-up that the manga half of
            this page is intentionally not yet wired. TV calendar grid below
            continues to render normally.
          */}
          <Alert kind={kinds.INFO}>
            <strong>{translate('CalendarComingInV2')}</strong>
            <div>{translate('CalendarComingInV2Hint')}</div>
          </Alert>

          {isMeasured ? <PageComponent totalItems={0} /> : <div />}
          {hasSeries && <Legend />}
        </PageContentBody>

        <CalendarLinkModal
          isOpen={isCalendarLinkModalOpen}
          onModalClose={handleGetCalendarLinkModalClose}
        />

        <CalendarOptionsModal
          isOpen={isOptionsModalOpen}
          onModalClose={handleOptionsModalClose}
        />
      </PageContent>
    </CalendarPageProvider>
  );
}

export default CalendarPage;

function CalendarPageProvider({
  episodeIds,
  episodeFileIds,
  children,
}: PropsWithChildren<{ episodeIds: number[]; episodeFileIds: number[] }>) {
  return (
    <QueueDetailsProvider episodeIds={episodeIds}>
      <EpisodeFileProvider episodeFileIds={episodeFileIds}>
        {children}
      </EpisodeFileProvider>
    </QueueDetailsProvider>
  );
}
