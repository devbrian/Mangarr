// Sonarr divergence: per Phase 7 D-10 + Lock #1 — mediaType prop added — see DIVERGENCE.md.
// Phase 15 Plan 15-07 Wave 3 (Sub-step F option (a)): mediaType prop default flipped 'series' ->
// 'manga' since TV is gone post-cutover; the prop type union 'series' | 'manga' collapsed to
// 'manga' (preserves the discriminator type for v2 reintroduction).
//
// GH issue #73 (2026-05-11) — QueueRow rewritten to consume MangaQueueItem shape
// directly; the `@ts-expect-error` directive on the spread and the legacy
// `useEpisodes` lookup (which would short-circuit on `episodeIds=[]` and
// gate the `isAllPopulated` flag forever) are removed. Mirrors the Plan 15-12-era
// History.tsx pattern that already skips `useEpisodes` for manga records.
import React, {
  ReactElement,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { SelectProvider, useSelect } from 'App/Select/SelectContext';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import FilterMenu from 'Components/Menu/FilterMenu';
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
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import { align, icons, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';
import { CheckInputChanged } from 'typings/inputs';
import MangaQueueItem from 'typings/MangaQueueItem';
import { TableOptionsChangePayload } from 'typings/Table';
import {
  registerPagePopulator,
  unregisterPagePopulator,
} from 'Utilities/pagePopulator';
import translate from 'Utilities/String/translate';
import QueueFilterModal from './QueueFilterModal';
import {
  setQueueOption,
  setQueueOptions,
  setQueueSort,
  useQueueOptions,
} from './queueOptionsStore';
import QueueRow from './QueueRow';
import RemoveQueueItemModal from './RemoveQueueItemModal';
import useQueueStatus from './Status/useQueueStatus';
import useQueue, {
  useFilters,
  useGrabQueueItems,
  useRemoveQueueItems,
} from './useQueue';

interface QueueProps {
  mediaType?: 'manga';
}

function renderEmptyAlert(
  selectedFilterKey: string | number,
  count: number,
  mediaType: 'manga'
) {
  let message: string = '';
  if (selectedFilterKey !== 'all' && count > 0) {
    message = translate('QueueFilterHasNoItems');
  } else if (mediaType === 'manga') {
    message = translate('QueueIsEmptyManga');
  } else {
    message = translate('QueueIsEmpty');
  }
  return <Alert kind={kinds.INFO}>{message}</Alert>;
}

function QueueContent({ mediaType = 'manga' }: QueueProps) {
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
  } = useQueue(mediaType);

  const { columns, pageSize, sortKey, sortDirection, selectedFilterKey } =
    useQueueOptions();

  const filters = useFilters();

  const { isRemoving, removeQueueItems } = useRemoveQueueItems();
  const { isGrabbing, grabQueueItems } = useGrabQueueItems();

  const { count } = useQueueStatus();

  const customFilters = useCustomFiltersList('queue');

  const isRefreshMonitoredDownloadsExecuting = useCommandExecuting(
    CommandNames.RefreshMonitoredDownloads
  );

  const shouldBlockRefresh = useRef(false);
  const currentQueue = useRef<ReactElement | null>(null);

  const { allSelected, allUnselected, selectAll, unselectAll, useSelectedIds } =
    useSelect<MangaQueueItem>();

  const selectedIds = useSelectedIds();
  const isPendingSelected = useMemo(() => {
    return records.some((item) => {
      return selectedIds.indexOf(item.id) > -1 && item.status === 'delay';
    });
  }, [records, selectedIds]);

  const [isConfirmRemoveModalOpen, setIsConfirmRemoveModalOpen] =
    useState(false);

  const [isInteractiveImportDownloadIds, setIsInteractiveImportDownloadIds] =
    useState<string[]>(() => []);

  const isRefreshing = isLoading || isRefreshMonitoredDownloadsExecuting;

  // Manga queue rows carry their own chapter/manga hydration via subresources
  // (or fall back to the `['/manga']` / `['/chapter']` React Query caches in
  // QueueRow). There is no episode-style multi-id batch lookup; the table is
  // populated as soon as the queue records arrive.
  const isAllPopulated = !isLoading;
  const hasError = error;
  const selectedCount = selectedIds.length;
  const disableSelectedActions = selectedCount === 0;

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

  const handleRefreshPress = useCallback(() => {
    executeCommand({
      name: CommandNames.RefreshMonitoredDownloads,
    });
  }, [executeCommand]);

  const handleQueueRowModalOpenOrClose = useCallback((isOpen: boolean) => {
    shouldBlockRefresh.current = isOpen;
  }, []);

  const handleGrabSelectedPress = useCallback(() => {
    grabQueueItems({ ids: selectedIds });
  }, [selectedIds, grabQueueItems]);

  const handleRemoveSelectedPress = useCallback(() => {
    shouldBlockRefresh.current = true;
    setIsConfirmRemoveModalOpen(true);
  }, [setIsConfirmRemoveModalOpen]);

  const handleRemoveSelectedConfirmed = useCallback(() => {
    shouldBlockRefresh.current = false;
    removeQueueItems({ ids: selectedIds });
    setIsConfirmRemoveModalOpen(false);
  }, [selectedIds, removeQueueItems]);

  const handleConfirmRemoveModalClose = useCallback(() => {
    shouldBlockRefresh.current = false;
    setIsConfirmRemoveModalOpen(false);
  }, []);

  const handleImportSelectedPress = useCallback(() => {
    shouldBlockRefresh.current = true;
    setIsInteractiveImportDownloadIds(
      selectedIds
        .map((id) => {
          const item = records.find((i) => i.id === id);

          return item?.downloadId;
        })
        .filter((id): id is string => !!id)
    );
  }, [records, selectedIds]);

  const handleImportSelectedModalClose = useCallback(() => {
    shouldBlockRefresh.current = false;
    setIsInteractiveImportDownloadIds([]);
  }, []);

  const handleFilterSelect = useCallback(
    (selectedFilterKey: string | number) => {
      setQueueOption('selectedFilterKey', selectedFilterKey);
    },
    []
  );

  const handleSortPress = useCallback(
    (sortKey: string, sortDirection?: SortDirection) => {
      setQueueSort({
        sortKey,
        sortDirection,
      });
    },
    []
  );

  const handleTableOptionChange = useCallback(
    (payload: TableOptionsChangePayload) => {
      setQueueOptions(payload);

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

    registerPagePopulator(repopulate);

    return () => {
      unregisterPagePopulator(repopulate);
    };
  }, [refetch]);

  if (!shouldBlockRefresh.current) {
    currentQueue.current = (
      <PageContentBody>
        {isRefreshing && !isAllPopulated ? <LoadingIndicator /> : null}

        {!isRefreshing && hasError ? (
          <Alert kind={kinds.DANGER}>{translate('QueueLoadError')}</Alert>
        ) : null}

        {isAllPopulated && !hasError && !records.length
          ? renderEmptyAlert(selectedFilterKey, count, mediaType)
          : null}

        {isAllPopulated && !hasError && !!records.length ? (
          // Phase 18 Plan-05: data-testid annotation per
          // .planning/phases/18-automated-ui-integration-test-suite-playwright-net/inventory/data-testid-spec.md.
          // Page-level testid lives on the outer wrapper; the inner div carries the
          // `manga-queue-table` testid so Playwright fixtures can scope row queries
          // beneath it without traversing through PageContent's `app-shell` testid.
          <div data-testid="manga-queue-page">
            <div data-testid="manga-queue-table">
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
                      <QueueRow
                        key={item.id}
                        columns={columns}
                        {...item}
                        onQueueRowModalOpenOrClose={
                          handleQueueRowModalOpenOrClose
                        }
                      />
                    );
                  })}
                </TableBody>
              </Table>
            </div>

            <TablePager
              page={page}
              totalPages={totalPages}
              totalRecords={totalRecords}
              isFetching={isFetching}
              onPageSelect={goToPage}
            />
          </div>
        ) : null}
      </PageContentBody>
    );
  }

  return (
    <PageContent title={translate('Queue')}>
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label="Refresh"
            iconName={icons.REFRESH}
            isSpinning={isRefreshing}
            onPress={handleRefreshPress}
          />

          <PageToolbarSeparator />

          <PageToolbarButton
            label={translate('GrabSelected')}
            iconName={icons.DOWNLOAD}
            isDisabled={disableSelectedActions || !isPendingSelected}
            isSpinning={isGrabbing}
            onPress={handleGrabSelectedPress}
          />

          <PageToolbarButton
            label={translate('RemoveSelected')}
            iconName={icons.REMOVE}
            isDisabled={disableSelectedActions}
            isSpinning={isRemoving}
            onPress={handleRemoveSelectedPress}
          />

          <PageToolbarSeparator />

          <PageToolbarButton
            label={translate('ImportSelected')}
            iconName={icons.INTERACTIVE}
            isDisabled={disableSelectedActions}
            onPress={handleImportSelectedPress}
          />
        </PageToolbarSection>

        <PageToolbarSection alignContent={align.RIGHT}>
          <TableOptionsModalWrapper
            columns={columns}
            pageSize={pageSize}
            maxPageSize={200}
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
            filterModalConnectorComponent={QueueFilterModal}
            onFilterSelect={handleFilterSelect}
          />
        </PageToolbarSection>
      </PageToolbar>

      {currentQueue.current}

      <RemoveQueueItemModal
        isOpen={isConfirmRemoveModalOpen}
        selectedCount={selectedCount}
        canChangeCategory={
          isConfirmRemoveModalOpen &&
          selectedIds.every((id: number) => {
            const item = records.find((i) => i.id === id);

            return !!(item && item.downloadClientHasPostImportCategory);
          })
        }
        canIgnore={
          isConfirmRemoveModalOpen &&
          selectedIds.every((id: number) => {
            const item = records.find((i) => i.id === id);

            return !!(item && item.mangaId && (item.chapterIds?.length ?? 0));
          })
        }
        isPending={
          isConfirmRemoveModalOpen &&
          selectedIds.every((id: number) => {
            const item = records.find((i) => i.id === id);

            if (!item) {
              return false;
            }

            return (
              item.status === 'delay' ||
              item.status === 'downloadClientUnavailable'
            );
          })
        }
        onRemovePress={handleRemoveSelectedConfirmed}
        onModalClose={handleConfirmRemoveModalClose}
      />

      <InteractiveImportModal
        isOpen={isInteractiveImportDownloadIds.length > 0}
        downloadIds={isInteractiveImportDownloadIds}
        title={translate('InteractiveImportMultipleQueueItems')}
        onModalClose={handleImportSelectedModalClose}
      />
    </PageContent>
  );
}

function Queue({ mediaType = 'manga' }: QueueProps) {
  const { records } = useQueue(mediaType);

  return (
    <SelectProvider<MangaQueueItem> items={records}>
      <QueueContent mediaType={mediaType} />
    </SelectProvider>
  );
}

export default Queue;
