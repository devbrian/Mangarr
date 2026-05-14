// Sonarr divergence: per Phase 7 D-10 + Lock #1 — mediaType prop added — see DIVERGENCE.md.
// Phase 15 Plan 15-07 Wave 3 (Sub-step F option (a)): mediaType prop default flipped 'series' ->
// 'manga' since TV is gone post-cutover; the prop type union 'series' | 'manga' collapsed to
// 'manga' (preserves the discriminator type for v2 reintroduction). Both default-sites
// (MissingContent inner + Missing outer) updated atomically.
// Sonarr divergence: Phase 17.3 Plan 17.3-13 (D-09 stub-importer cascade) — Episode/Episode
// stub import rewritten to Chapter/Chapter peer; type alias Episode -> Chapter in body.
// The Phase-15 historical NOTE below is preserved as repo memory of the pre-rename state.
// NOTE: Episode imports below are orphaned post-Plan 15-07 Task 1 (frontend/src/Episode/ deleted)
// and contribute to the expected ~247-error TS2307 cascade — Plan 15-08/15-09 resolves this.
//
// Debug session wanted-missing-zero-rows (GH issue #48, 2026-05-10): MissingRow
// rewritten to consume Chapter shape (was reading Series subresource the manga
// backend never hydrates → empty tbody). The `@ts-expect-error` directive on
// the spread previously needed because MissingRow expected TV props; now
// MissingRowProps extends Chapter, and Episode (the records type) extends
// Chapter, so the spread type-checks cleanly without the directive.
//
// GH issue #63 (2026-05-10) — Wanted bulk toolbar rewire: the Phase-15 no-op stub
// `useToggleEpisodesMonitored` was replaced with `useBulkToggleChaptersMonitored`
// (frontend/src/Chapter/useChapter.ts — PUT /api/v5/chapter/monitor); the TV-side
// `CommandNames.EpisodeSearch` / `MissingEpisodeSearch` dispatches were retargeted
// to the manga-side `CommandNames.ChapterSearch` / `MissingChapterSearch` whose
// IExecute<T> handlers are wired in src/NzbDrone.Core/IndexerSearch/Manga/. The
// `episodeIds` payload field renames to `chapterIds` to match
// `ChapterSearchCommand { List<int> ChapterIds }`.
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import QueueDetailsProvider from 'Activity/Queue/Details/QueueDetailsProvider';
import { SelectProvider, useSelect } from 'App/Select/SelectContext';
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
import Chapter from 'Chapter/Chapter';
import { Filter } from 'Filters/Filter';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import { align, icons, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';
import { CheckInputChanged } from 'typings/inputs';
import { TableOptionsChangePayload } from 'typings/Table';
import getFilterValue from 'Utilities/Filter/getFilterValue';
import selectUniqueIds from 'Utilities/Object/selectUniqueIds';
import {
  registerPagePopulator,
  unregisterPagePopulator,
} from 'Utilities/pagePopulator';
import translate from 'Utilities/String/translate';
import MissingFilterModal from './MissingFilterModal';
import {
  setMissingOption,
  setMissingOptions,
  setMissingSort,
  useMissingOptions,
} from './missingOptionsStore';
import MissingRow from './MissingRow';
import useMissing, { FILTERS, useFilters } from './useMissing';

interface MissingProps {
  mediaType?: 'manga';
}

function getMonitoredValue(
  filters: Filter[],
  selectedFilterKey: string | number
): boolean {
  return !!getFilterValue(filters, selectedFilterKey, 'monitored', false);
}

function MissingContent({ mediaType = 'manga' }: MissingProps) {
  const executeCommand = useExecuteCommand();

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
  } = useMissing(mediaType);

  const { columns, pageSize, sortKey, sortDirection, selectedFilterKey } =
    useMissingOptions();

  const filters = useFilters();
  const customFilters = useCustomFiltersList('wanted.missing');

  const isSearchingForAllEpisodes = useCommandExecuting(
    CommandNames.MissingChapterSearch
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

  const [isInteractiveImportModalOpen, setIsInteractiveImportModalOpen] =
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

  const handleConfirmSearchAllMissingModalClose = useCallback(() => {
    setIsConfirmSearchAllModalOpen(false);
  }, []);

  const handleSearchAllMissingConfirmed = useCallback(() => {
    executeCommand(
      {
        name: CommandNames.MissingChapterSearch,
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

  const handleInteractiveImportPress = useCallback(() => {
    setIsInteractiveImportModalOpen(true);
  }, []);

  const handleInteractiveImportModalClose = useCallback(() => {
    setIsInteractiveImportModalOpen(false);
  }, []);

  const handleFilterSelect = useCallback((filterKey: number | string) => {
    setMissingOption('selectedFilterKey', filterKey);
  }, []);

  const handleSortPress = useCallback(
    (sortKey: string, sortDirection?: SortDirection) => {
      setMissingSort({
        sortKey,
        sortDirection,
      });
    },
    []
  );

  const handleTableOptionChange = useCallback(
    (payload: TableOptionsChangePayload) => {
      setMissingOptions(payload);

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
    <QueueDetailsProvider episodeIds={episodeIds}>
      <PageContent title={translate('Missing')}>
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

            {/* Phase 12 Plan 12-11 LOCK guard — sub-wave-B-addition (audit row 4 secondary closure):
                InteractiveImport flow is TV-only in v1; the manga-side useInteractiveImport
                discriminator-extension is v1.1-deferred (Plan 12-98 promoted PRIMARY entry to
                v1.1-roadmap.md per D-12-19). This guard prevents the silently-broken UI exposure
                identified by Plan 12-06 audit `## Manga sibling reachability` first row — the
                manga user clicking "Manual Import" today would land at TV-only /manualimport
                endpoint with TV-only response shapes. v1.1+ deletion: when the manga InteractiveImport
                discriminator-extension ships, drop this guard as the FIRST task of that v1.1 plan
                (per audit `### v1.1-roadmap.md entry skeleton — PRIMARY` Wiring sites line).
                Guarded for v1 — drop when Phase 12-98 v1.1+ InteractiveImport discriminator extension lands.
                NOTE: Two adjacent ternaries (not a Fragment) because PageToolbarSection's TS prop
                signature accepts only `ReactElement<PageToolbarButtonProps> | ReactElement<never> | null`
                children — Fragments would also break the section's separator-detection heuristic. */}
            {mediaType !== 'manga' ? <PageToolbarSeparator /> : null}

            {mediaType !== 'manga' ? (
              <PageToolbarButton
                label={translate('ManualImport')}
                iconName={icons.INTERACTIVE}
                onPress={handleInteractiveImportPress}
              />
            ) : null}
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
              filters={filters}
              customFilters={customFilters}
              filterModalConnectorComponent={MissingFilterModal}
              onFilterSelect={handleFilterSelect}
            />
          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody>
          <div data-testid="manga-missing-page">
          {isFetching && isLoading ? <LoadingIndicator /> : null}

          {!isFetching && error ? (
            <Alert kind={kinds.DANGER}>{translate('MissingLoadError')}</Alert>
          ) : null}

          {!isLoading && !error && !records.length ? (
            <Alert kind={kinds.INFO}>
              {mediaType === 'manga'
                ? translate('NothingWanted')
                : translate('MissingNoItems')}
            </Alert>
          ) : null}

          {!isLoading && !error && !!records.length ? (
            <div data-testid="manga-missing-table">
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
                      <MissingRow key={item.id} columns={columns} {...item} />
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
                title={translate('SearchForAllMissingChapters')}
                message={
                  <div>
                    <div>
                      {translate(
                        'SearchForAllMissingChaptersConfirmationCount',
                        {
                          totalRecords,
                        }
                      )}
                    </div>
                    <div>{translate('MassSearchCancelWarning')}</div>
                  </div>
                }
                confirmLabel={translate('Search')}
                onConfirm={handleSearchAllMissingConfirmed}
                onCancel={handleConfirmSearchAllMissingModalClose}
              />
            </div>
          ) : null}
          </div>
        </PageContentBody>

        {/* Phase 12 Plan 12-11 LOCK guard — sub-wave-B-addition (audit row 4 secondary closure):
            See toolbar-button guard above — same v1 LOCK rationale; same v1.1+ deletion path.
            Guarded for v1 — drop when Phase 12-98 v1.1+ InteractiveImport discriminator extension lands. */}
        {mediaType !== 'manga' ? (
          <InteractiveImportModal
            isOpen={isInteractiveImportModalOpen}
            onModalClose={handleInteractiveImportModalClose}
          />
        ) : null}
      </PageContent>
    </QueueDetailsProvider>
  );
}

function Missing({ mediaType = 'manga' }: MissingProps) {
  const { records } = useMissing(mediaType);

  return (
    <SelectProvider<Chapter> items={records}>
      <MissingContent mediaType={mediaType} />
    </SelectProvider>
  );
}

export default Missing;
