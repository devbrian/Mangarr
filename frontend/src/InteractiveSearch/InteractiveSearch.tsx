import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import FilterMenu from 'Components/Menu/FilterMenu';
import PageMenuButton from 'Components/Menu/PageMenuButton';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import { align, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import InteractiveSearchFilterModal from './InteractiveSearchFilterModal';
import InteractiveSearchPayload from './InteractiveSearchPayload';
import InteractiveSearchRow from './InteractiveSearchRow';
import InteractiveSearchType from './InteractiveSearchType';
import { setReleaseOption, useReleaseOptions } from './releaseOptionsStore';
import useReleases, { FILTERS, setReleaseSort } from './useReleases';
import styles from './InteractiveSearch.css';

interface InteractiveSearchProps {
  type: InteractiveSearchType;
  searchPayload: InteractiveSearchPayload;
}

function InteractiveSearch({ type, searchPayload }: InteractiveSearchProps) {
  const customFilters = useCustomFiltersList('releases');
  const { columns } = useReleaseOptions();

  const {
    isFetching,
    isFetched,
    error,
    data,
    totalItems,
    selectedFilterKey,
    sortKey,
    sortDirection,
  } = useReleases(searchPayload);

  const handleFilterSelect = useCallback(
    (selectedFilterKey: string | number) => {
      if (type === 'episode') {
        setReleaseOption('episodeSelectedFilterKey', selectedFilterKey);
      } else if (type === 'chapter') {
        setReleaseOption('chapterSelectedFilterKey', selectedFilterKey);
      } else if (type === 'manga') {
        setReleaseOption('mangaSelectedFilterKey', selectedFilterKey);
      } else {
        setReleaseOption('seasonSelectedFilterKey', selectedFilterKey);
      }
    },
    [type]
  );

  const handleSortPress = useCallback(
    (sortKey: string, sortDirection?: SortDirection) => {
      setReleaseSort(sortKey, sortDirection);
    },
    []
  );

  const errorMessage = getErrorMessage(error);

  return (
    // Phase 18 Plan-08 D-18 -- `interactive-search-modal` testid wraps the
    // entire InteractiveSearch content surface. The plan-prescribed wrapper
    // name presumed a dedicated `InteractiveSearchModal.tsx` file (Sonarr
    // shape); the Mangarr architecture renders this content EITHER as a
    // tab-pane in MangaDetails (`activeTab === 'search'`) OR wrapped in
    // `ChapterDetailsModal`. Annotating the content surface lets PageObject
    // GetByTestId("interactive-search-modal") resolve in both contexts.
    <div data-testid="interactive-search-modal">
      <div className={styles.filterMenuContainer}>
        <FilterMenu
          alignMenu={align.RIGHT}
          selectedFilterKey={selectedFilterKey}
          filters={FILTERS}
          customFilters={customFilters}
          buttonComponent={PageMenuButton}
          filterModalConnectorComponent={InteractiveSearchFilterModal}
          filterModalConnectorComponentProps={{ type, searchPayload }}
          onFilterSelect={handleFilterSelect}
        />
      </div>

      {isFetching ? <LoadingIndicator /> : null}

      {!isFetching && error ? (
        <div>
          {errorMessage ? (
            <>
              {translate('InteractiveSearchResultsMangaFailedErrorMessage', {
                message:
                  errorMessage.charAt(0).toLowerCase() + errorMessage.slice(1),
              })}
            </>
          ) : (
            translate('EpisodeSearchResultsLoadError')
          )}
        </div>
      ) : null}

      {!isFetching && isFetched && !totalItems ? (
        // Phase 18 Plan-08 -- `interactive-search-modal-no-results` testid.
        // Alert component destructures explicit props per Alert.tsx and does
        // not pass through data-testid; wrap with a div to expose the state
        // assertion target.
        <div data-testid="interactive-search-modal-no-results">
          <Alert kind={kinds.INFO}>{translate('NoResultsFound')}</Alert>
        </div>
      ) : null}

      {!!totalItems && !isFetching && !data.length ? (
        <Alert kind={kinds.WARNING}>
          {translate('AllResultsAreHiddenByTheAppliedFilter')}
        </Alert>
      ) : null}

      {!isFetching && !!data.length ? (
        // Phase 18 Plan-08 -- `interactive-search-modal-table` testid wrapper.
        // Same wrapper-div pattern: Table destructures explicit props and does
        // not pass through data-testid. Wrap to expose the selector.
        <div data-testid="interactive-search-modal-table">
          <Table
            columns={columns}
            sortKey={sortKey}
            sortDirection={sortDirection}
            onSortPress={handleSortPress}
          >
            <TableBody>
              {data.map((item) => {
                return (
                  <InteractiveSearchRow
                    key={`${item.release.indexerId}-${item.release.guid}`}
                    {...item}
                    searchPayload={searchPayload}
                  />
                );
              })}
            </TableBody>
          </Table>
        </div>
      ) : null}

      {!isFetching && totalItems !== data.length && !!data.length ? (
        <div className={styles.filteredMessage}>
          {translate('SomeResultsAreHiddenByTheAppliedFilter')}
        </div>
      ) : null}
    </div>
  );
}

export default InteractiveSearch;
