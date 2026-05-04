// Sonarr divergence: per Phase 7 D-10 + Lock #1 — mediaType prop added — see DIVERGENCE.md.
// Existing TV call-site (`/activity/history` route → <History />`) preserved verbatim via the
// default mediaType='series'. Manga call-site is the thin wrapper MangaHistory.tsx (Plan 07-09
// task 2) invoking <History mediaType="manga" /> for the /manga/activity/history route. Phase 8
// collapses.
import React, { useCallback, useEffect, useMemo } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import FilterMenu from 'Components/Menu/FilterMenu';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import TablePager from 'Components/Table/TablePager';
import useEpisodes from 'Episode/useEpisodes';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import { align, icons, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import HistoryItem from 'typings/History';
import { TableOptionsChangePayload } from 'typings/Table';
import selectUniqueIds from 'Utilities/Object/selectUniqueIds';
import {
  registerPagePopulator,
  unregisterPagePopulator,
} from 'Utilities/pagePopulator';
import translate from 'Utilities/String/translate';
import HistoryFilterModal from './HistoryFilterModal';
import {
  setHistoryOption,
  setHistoryOptions,
  setHistorySort,
  useHistoryOptions,
} from './historyOptionsStore';
import HistoryRow from './HistoryRow';
import useHistory, { useFilters } from './useHistory';

interface HistoryProps {
  mediaType?: 'series' | 'manga';
}

function History({ mediaType = 'series' }: HistoryProps) {
  const {
    records,
    totalPages,
    totalRecords,
    error,
    isFetching,
    isFetched,
    isLoading,
    page,
    goToPage,
    refetch,
  } = useHistory(mediaType);

  const { columns, pageSize, sortKey, sortDirection, selectedFilterKey } =
    useHistoryOptions();

  const episodeIds = useMemo(() => {
    return selectUniqueIds<HistoryItem, number>(records, 'episodeId');
  }, [records]);

  const {
    isFetching: isEpisodesFetching,
    isFetched: isEpisodesFetched,
    error: episodesError,
  } = useEpisodes({ episodeIds });

  const filters = useFilters();

  const customFilters = useCustomFiltersList('history');

  const isFetchingAny = isLoading || isEpisodesFetching;
  const isAllPopulated = isFetched && (isEpisodesFetched || !records.length);
  const hasError = error || episodesError;

  const handleFilterSelect = useCallback(
    (selectedFilterKey: string | number) => {
      setHistoryOption('selectedFilterKey', selectedFilterKey);
    },
    []
  );

  const handleSortPress = useCallback(
    (sortKey: string, sortDirection?: SortDirection) => {
      setHistorySort({
        sortKey,
        sortDirection,
      });
    },
    []
  );

  const handleTableOptionChange = useCallback(
    (payload: TableOptionsChangePayload) => {
      setHistoryOptions(payload);

      if (payload.pageSize) {
        goToPage(1);
      }
    },
    [goToPage]
  );

  const handleRefreshPress = useCallback(() => {
    goToPage(1);
    refetch();
  }, [goToPage, refetch]);

  useEffect(() => {
    const repopulate = () => {
      refetch();
    };

    registerPagePopulator(repopulate);

    return () => {
      unregisterPagePopulator(repopulate);
    };
  }, [refetch]);

  return (
    <PageContent title={translate('History')}>
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label={translate('Refresh')}
            iconName={icons.REFRESH}
            isSpinning={isFetching}
            onPress={handleRefreshPress}
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
            filters={filters}
            customFilters={customFilters}
            filterModalConnectorComponent={HistoryFilterModal}
            onFilterSelect={handleFilterSelect}
          />
        </PageToolbarSection>
      </PageToolbar>

      <PageContentBody>
        {isFetchingAny && !isAllPopulated ? <LoadingIndicator /> : null}

        {!isFetchingAny && hasError ? (
          <Alert kind={kinds.DANGER}>{translate('HistoryLoadError')}</Alert>
        ) : null}

        {
          // If history isPopulated and it's empty show no history found and don't
          // wait for the episodes to populate because they are never coming.

          isFetched && !hasError && !records.length ? (
            <Alert kind={kinds.INFO}>
              {mediaType === 'manga'
                ? translate('NoHistoryFoundManga')
                : translate('NoHistoryFound')}
            </Alert>
          ) : null
        }

        {isAllPopulated && !hasError && records.length ? (
          <div>
            <Table
              columns={columns}
              pageSize={pageSize}
              sortKey={sortKey}
              sortDirection={sortDirection}
              onTableOptionChange={handleTableOptionChange}
              onSortPress={handleSortPress}
            >
              <TableBody>
                {records.map((item) => {
                  return (
                    <HistoryRow key={item.id} columns={columns} {...item} />
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
          </div>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default History;
