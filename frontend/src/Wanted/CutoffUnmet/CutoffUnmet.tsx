// Sonarr divergence: per Phase 7 D-10 + Lock #1 (Plan 12-08 sub-wave C extension) — mediaType prop added — see DIVERGENCE.md.
// Phase 15 Plan 15-07 Wave 3 (Sub-step F option (a)): mediaType prop default flipped 'series' ->
// 'manga' since TV is gone post-cutover; the prop type union 'series' | 'manga' collapsed to
// 'manga' (preserves the discriminator type for v2 reintroduction). Both default-sites
// (CutoffUnmetContent inner + CutoffUnmet outer) updated atomically.
// Sonarr divergence: Phase 17.3 Plan 17.3-13 (D-09 stub-importer cascade) —
// Episode/Episode stub import rewritten to Chapter/Chapter peer; EpisodeFile/EpisodeFileProvider
// stub dropped (no-op pass-through wrapper; children rendered directly). The Phase-15
// historical NOTE below is preserved as repo memory of the pre-rename state.
// NOTE: Episode + EpisodeFile imports below are orphaned post-Plan 15-07 Task 1
// (frontend/src/{Episode,EpisodeFile}/ deleted) and contribute to the expected ~247-error
// TS2307 cascade — Plan 15-08/15-09 resolves this.
//
// Closest analog: frontend/src/Wanted/Missing/Missing.tsx (Plan 07-10) — same pattern.
//
// Debug session wanted-missing-zero-rows (GH issue #48 sibling impact, 2026-05-10):
// CutoffUnmetRow rewritten to consume Chapter shape; the `@ts-expect-error` directive on
// the spread is no longer needed because CutoffUnmetRowProps extends Chapter and Episode
// (the records type) extends Chapter — the spread type-checks cleanly without the directive.
//
// GH issue #63 (2026-05-10) — Wanted bulk toolbar rewire: parallel sibling change to
// Missing.tsx — `useToggleEpisodesMonitored` no-op stub replaced with
// `useBulkToggleChaptersMonitored` (frontend/src/Chapter/useChapter.ts — PUT
// /api/v5/chapter/monitor); TV-side `CommandNames.EpisodeSearch` /
// `CutoffUnmetEpisodeSearch` retargeted to the manga-side `CommandNames.ChapterSearch` /
// `CutoffUnmetChapterSearch` whose IExecute<T> handlers live in
// src/NzbDrone.Core/IndexerSearch/Manga/. `episodeIds` payload field renames to `chapterIds`.
import React, {
  PropsWithChildren,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from 'react';
import QueueDetailsProvider from 'Activity/Queue/Details/QueueDetailsProvider';
import { SelectProvider, useSelect } from 'App/Select/SelectContext';
import Chapter from 'Chapter/Chapter';
import { useBulkToggleChaptersMonitored } from 'Chapter/useChapter';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import FilterMenu from 'Components/Menu/FilterMenu';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import TablePager from 'Components/Table/TablePager';
import { Filter } from 'Filters/Filter';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import { align, icons, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import { CheckInputChanged } from 'typings/inputs';
import { TableOptionsChangePayload } from 'typings/Table';
import getFilterValue from 'Utilities/Filter/getFilterValue';
import selectUniqueIds from 'Utilities/Object/selectUniqueIds';
import {
  registerPagePopulator,
  unregisterPagePopulator,
} from 'Utilities/pagePopulator';
import translate from 'Utilities/String/translate';
import {
  setCutoffUnmetOption,
  setCutoffUnmetOptions,
  setCutoffUnmetSort,
  useCutoffUnmetOptions,
} from './cutoffUnmetOptionsStore';
import CutoffUnmetFilterModal from './CutoffUnmetFilterModal';
import CutoffUnmetRow from './CutoffUnmetRow';
import useCutoffUnmet, { FILTERS } from './useCutoffUnmet';

interface CutoffUnmetProps {
  mediaType?: 'manga';
}

function getMonitoredValue(
  filters: Filter[],
  selectedFilterKey: string | number
): boolean {
  return !!getFilterValue(filters, selectedFilterKey, 'monitored', false);
}

function CutoffUnmetContent({ mediaType = 'manga' }: CutoffUnmetProps) {
  const executeCommand = useExecuteCommand();
  const customFilters = useCustomFiltersList('wanted.cutoffUnmet');

  const {
    records,
    totalPages,
    totalRecords,
    error,
    isFetching,
    isLoading,
    page,
    goToPage,
    refetch,
  } = useCutoffUnmet(mediaType);

  const { columns, pageSize, sortKey, sortDirection, selectedFilterKey } =
    useCutoffUnmetOptions();

  const isSearchingForAllEpisodes = useCommandExecuting(
    CommandNames.CutoffUnmetChapterSearch
  );
  const isSearchingForSelectedEpisodes = useCommandExecuting(
    CommandNames.ChapterSearch
  );

  const {
    allSelected,
    allUnselected,
    anySelected,
    getSelectedIds,
    selectAll,
    unselectAll,
  } = useSelect<Chapter>();

  const [isConfirmSearchAllModalOpen, setIsConfirmSearchAllModalOpen] =
    useState(false);

  const {
    bulkToggleChaptersMonitored,
    isBulkToggling: isToggling,
  } = useBulkToggleChaptersMonitored();

  const isShowingMonitored = getMonitoredValue(FILTERS, selectedFilterKey);
  const isSearchingForEpisodes =
    isSearchingForAllEpisodes || isSearchingForSelectedEpisodes;

  const episodeIds = useMemo(() => {
    return selectUniqueIds<Chapter, number>(records, 'id');
  }, [records]);

  const episodeFileIds = useMemo(() => {
    // @ts-expect-error — TV-shape episodeFileId field. Plan 15-12.
    return selectUniqueIds<Chapter, number>(records, 'episodeFileId');
  }, [records]);

  const handleSelectAllChange = useCallback(
    ({ value }: CheckInputChanged) => {
      if (value) {
        selectAll();
      } else {
        unselectAll();
      }
    },
    [selectAll, unselectAll]
  );

  const handleSearchSelectedPress = useCallback(() => {
    executeCommand(
      {
        name: CommandNames.ChapterSearch,
        chapterIds: getSelectedIds(),
      },
      () => {
        refetch();
      }
    );
  }, [getSelectedIds, executeCommand, refetch]);

  const handleSearchAllPress = useCallback(() => {
    setIsConfirmSearchAllModalOpen(true);
  }, []);

  const handleConfirmSearchAllCutoffUnmetModalClose = useCallback(() => {
    setIsConfirmSearchAllModalOpen(false);
  }, []);

  const handleSearchAllCutoffUnmetConfirmed = useCallback(() => {
    executeCommand(
      {
        name: CommandNames.CutoffUnmetChapterSearch,
      },
      () => {
        refetch();
      }
    );

    setIsConfirmSearchAllModalOpen(false);
  }, [executeCommand, refetch]);

  const handleToggleSelectedPress = useCallback(() => {
    bulkToggleChaptersMonitored({
      chapterIds: getSelectedIds(),
      monitored: !isShowingMonitored,
    });
  }, [isShowingMonitored, getSelectedIds, bulkToggleChaptersMonitored]);

  const handleFilterSelect = useCallback((filterKey: number | string) => {
    setCutoffUnmetOption('selectedFilterKey', filterKey);
  }, []);

  const handleSortPress = useCallback(
    (sortKey: string, sortDirection?: SortDirection) => {
      setCutoffUnmetSort({
        sortKey,
        sortDirection,
      });
    },
    []
  );

  const handleTableOptionChange = useCallback(
    (payload: TableOptionsChangePayload) => {
      setCutoffUnmetOptions(payload);

      if (payload.pageSize) {
        goToPage(1);
      }
    },
    [goToPage]
  );

  useEffect(() => {
    const repopulate = () => {
      refetch();
    };

    registerPagePopulator(repopulate, [
      'seriesUpdated',
      'episodeFileUpdated',
      'episodeFileDeleted',
    ]);

    return () => {
      unregisterPagePopulator(repopulate);
    };
  }, [refetch]);

  return (
    <CutoffUnmetProvider
      episodeIds={episodeIds}
      episodeFileIds={episodeFileIds}
    >
      <PageContent title={translate('CutoffUnmet')}>
        <PageToolbar>
          <PageToolbarSection>
            <PageToolbarButton
              label={
                anySelected
                  ? translate('SearchSelected')
                  : translate('SearchAll')
              }
              iconName={icons.SEARCH}
              isDisabled={isSearchingForEpisodes}
              isSpinning={isSearchingForEpisodes}
              onPress={
                anySelected ? handleSearchSelectedPress : handleSearchAllPress
              }
            />

            <PageToolbarSeparator />

            <PageToolbarButton
              label={
                isShowingMonitored
                  ? translate('UnmonitorSelected')
                  : translate('MonitorSelected')
              }
              iconName={icons.MONITORED}
              isDisabled={!anySelected}
              isSpinning={isToggling}
              onPress={handleToggleSelectedPress}
            />
          </PageToolbarSection>

          <PageToolbarSection alignContent={align.RIGHT}>
            <TableOptionsModalWrapper
              columns={columns}
              pageSize={pageSize}
              onTableOptionChange={handleTableOptionChange}
            >
              <PageToolbarButton
                label={translate('Options')}
                iconName={icons.TABLE}
              />
            </TableOptionsModalWrapper>

            <FilterMenu
              alignMenu={align.RIGHT}
              selectedFilterKey={selectedFilterKey}
              filters={FILTERS}
              customFilters={customFilters}
              filterModalConnectorComponent={CutoffUnmetFilterModal}
              onFilterSelect={handleFilterSelect}
            />
          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody>
          {isFetching && isLoading ? <LoadingIndicator /> : null}

          {!isFetching && error ? (
            <Alert kind={kinds.DANGER}>
              {translate('CutoffUnmetLoadError')}
            </Alert>
          ) : null}

          {!isLoading && !error && !records.length ? (
            <Alert kind={kinds.INFO}>
              {mediaType === 'manga'
                ? translate('NothingWanted')
                : translate('CutoffUnmetNoItems')}
            </Alert>
          ) : null}

          {!isLoading && !error && !!records.length ? (
            <div>
              <Table
                selectAll={true}
                allSelected={allSelected}
                allUnselected={allUnselected}
                columns={columns}
                pageSize={pageSize}
                sortKey={sortKey}
                sortDirection={sortDirection}
                onTableOptionChange={handleTableOptionChange}
                onSelectAllChange={handleSelectAllChange}
                onSortPress={handleSortPress}
              >
                <TableBody>
                  {records.map((item) => {
                    return (
                      <CutoffUnmetRow
                        key={item.id}
                        columns={columns}
                        {...item}
                      />
                    );
                  })}
                </TableBody>
              </Table>

              <TablePager
                page={page}
                totalPages={totalPages}
                totalRecords={totalRecords}
                isFetching={isFetching}
                onPageSelect={goToPage}
              />

              <ConfirmModal
                isOpen={isConfirmSearchAllModalOpen}
                kind={kinds.DANGER}
                title={translate('SearchForCutoffUnmetEpisodes')}
                message={
                  <div>
                    <div>
                      {translate(
                        'SearchForCutoffUnmetEpisodesConfirmationCount',
                        { totalRecords }
                      )}
                    </div>
                    <div>{translate('MassSearchCancelWarning')}</div>
                  </div>
                }
                confirmLabel={translate('Search')}
                onConfirm={handleSearchAllCutoffUnmetConfirmed}
                onCancel={handleConfirmSearchAllCutoffUnmetModalClose}
              />
            </div>
          ) : null}
        </PageContentBody>
      </PageContent>
    </CutoffUnmetProvider>
  );
}

export default function CutoffUnmet({
  mediaType = 'manga',
}: CutoffUnmetProps) {
  const { records } = useCutoffUnmet(mediaType);

  return (
    <SelectProvider<Chapter> items={records}>
      <CutoffUnmetContent mediaType={mediaType} />
    </SelectProvider>
  );
}

function CutoffUnmetProvider({
  episodeIds,
  episodeFileIds: _episodeFileIds,
  children,
}: PropsWithChildren<{ episodeIds: number[]; episodeFileIds: number[] }>) {
  // Sonarr divergence: Phase 17.3 Plan 17.3-13 — EpisodeFile/EpisodeFileProvider stub
  // dropped (no-op pass-through wrapper; children rendered directly). The wrapper was
  // a Phase 15 Plan 15-12 stub that returned <>{children}</> verbatim; the manga side
  // has no EpisodeFile aggregate to provide cache context for. episodeFileIds prop is
  // kept on the wrapper signature for caller-shape compatibility but no longer fed
  // anywhere — Phase 8 cleanup: drop the prop + the wrapper outright when the manga
  // ChapterFile cache-provider lands (or never, if the URL-keyed cache pattern obviates it).
  return (
    <QueueDetailsProvider episodeIds={episodeIds}>
      {children}
    </QueueDetailsProvider>
  );
}
