import React, { useCallback, useMemo, useRef, useState } from 'react';
import QueueDetailsProvider from 'Activity/Queue/Details/QueueDetailsProvider';
import { useAppDimension } from 'App/appStore';
import { SelectProvider } from 'App/Select/SelectContext';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageJumpBar, { PageJumpBarItems } from 'Components/Page/PageJumpBar';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import { align, icons, kinds } from 'Helpers/Props';
import { DESCENDING } from 'Helpers/Props/sortDirections';
import ParseToolbarButton from 'Parse/ParseToolbarButton';
import NoManga from 'Manga/NoManga';
// Sonarr divergence: NEW manga sibling per Phase 7 D-01/D-02 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Index/SeriesIndex.tsx (verbatim port).
//
// Manga sibling preserves: 3-view (Posters/Overview/Table) toggle, page toolbar shape,
// filter/sort/view menus, select-mode footer, refresh button, jump-bar character index.
//
// Manga sibling diverges from SeriesIndex:
//   * Imports useMangaIndex / useMangaOptions / setMangaSort from Manga/ peers.
//   * Routes via /manga (not /).
//   * Manga has no Season/Episode columns; chapter progress derived from chapter list.
//   * Translation language replaces Network/OriginalLanguage in column set.
//
// Phase 8 cleanup: when Series/ deletes, this becomes the canonical Index page.
import {
  setMangaOption,
  setMangaSort,
  setMangaTableOptions,
  useMangaOptions,
} from 'Manga/mangaOptionsStore';
import { FILTERS, useMangaIndex } from 'Manga/useManga';
import { TableOptionsChangePayload } from 'typings/Table';
import translate from 'Utilities/String/translate';
import MangaIndexFilterMenu from './Menus/MangaIndexFilterMenu';
import MangaIndexSortMenu from './Menus/MangaIndexSortMenu';
import MangaIndexViewMenu from './Menus/MangaIndexViewMenu';
import MangaIndexOverviewOptionsModal from './Overview/Options/MangaIndexOverviewOptionsModal';
import MangaIndexOverviews from './Overview/MangaIndexOverviews';
import MangaIndexPosterOptionsModal from './Posters/Options/MangaIndexPosterOptionsModal';
import MangaIndexPosters from './Posters/MangaIndexPosters';
import MangaIndexSelectAllButton from './Select/MangaIndexSelectAllButton';
import MangaIndexSelectAllMenuItem from './Select/MangaIndexSelectAllMenuItem';
import MangaIndexSelectFooter from './Select/MangaIndexSelectFooter';
import MangaIndexSelectModeButton from './Select/MangaIndexSelectModeButton';
import MangaIndexSelectModeMenuItem from './Select/MangaIndexSelectModeMenuItem';
import MangaIndexFooter from './MangaIndexFooter';
import MangaIndexRefreshMangaButton from './MangaIndexRefreshMangaButton';
import MangaIndexTable from './Table/MangaIndexTable';
import MangaIndexTableOptions from './Table/MangaIndexTableOptions';
import styles from './MangaIndex.css';

function getViewComponent(view: string) {
  if (view === 'posters') {
    return MangaIndexPosters;
  }

  if (view === 'overview') {
    return MangaIndexOverviews;
  }

  return MangaIndexTable;
}

function MangaIndex() {
  const {
    isLoading: isFetching,
    isFetched,
    isError: error,
    data,
    totalItems,
  } = useMangaIndex();

  const { selectedFilterKey, sortKey, sortDirection, view, columns } =
    useMangaOptions();
  const filters = FILTERS;

  const customFilters = useCustomFiltersList('manga');

  const executeCommand = useExecuteCommand();
  const isRssSyncExecuting = useCommandExecuting(CommandNames.RssSync);
  const isSmallScreen = useAppDimension('isSmallScreen');
  const scrollerRef = useRef<HTMLDivElement>(null);
  const [isOptionsModalOpen, setIsOptionsModalOpen] = useState(false);
  const [jumpToCharacter, setJumpToCharacter] = useState<string | undefined>(
    undefined
  );
  const [isSelectMode, setIsSelectMode] = useState(false);

  const onRssSyncPress = useCallback(() => {
    executeCommand({
      name: CommandNames.RssSync,
    });
  }, [executeCommand]);

  const onSelectModePress = useCallback(() => {
    setIsSelectMode(!isSelectMode);
  }, [isSelectMode, setIsSelectMode]);

  const onTableOptionChange = useCallback(
    (
      payload: TableOptionsChangePayload & {
        tableOptions?: { showBanners?: boolean; showSearchAction?: boolean };
      }
    ) => {
      if (payload.tableOptions) {
        setMangaTableOptions(payload.tableOptions);
      } else if (payload.columns) {
        setMangaOption('columns', payload.columns);
      }
    },
    []
  );

  const onViewSelect = useCallback(
    (value: string) => {
      setMangaOption('view', value);

      if (scrollerRef.current) {
        scrollerRef.current.scrollTo(0, 0);
      }
    },
    [scrollerRef]
  );

  const onSortSelect = useCallback((value: string) => {
    setMangaSort({ sortKey: value });
  }, []);

  const onFilterSelect = useCallback((value: string | number) => {
    setMangaOption('selectedFilterKey', value);
  }, []);

  const onOptionsPress = useCallback(() => {
    setIsOptionsModalOpen(true);
  }, [setIsOptionsModalOpen]);

  const onOptionsModalClose = useCallback(() => {
    setIsOptionsModalOpen(false);
  }, [setIsOptionsModalOpen]);

  const onJumpBarItemPress = useCallback(
    (character: string) => {
      setJumpToCharacter(character);
    },
    [setJumpToCharacter]
  );

  const onScroll = useCallback(() => {
    setJumpToCharacter(undefined);
  }, [setJumpToCharacter]);

  const jumpBarItems: PageJumpBarItems = useMemo(() => {
    // Reset if not sorting by sortTitle
    if (sortKey !== 'sortTitle') {
      return {
        characters: {},
        order: [],
      };
    }

    const characters = data.reduce((acc: Record<string, number>, item) => {
      let char = item.sortTitle.charAt(0);

      if (!isNaN(Number(char))) {
        char = '#';
      }

      if (char in acc) {
        acc[char] = acc[char] + 1;
      } else {
        acc[char] = 1;
      }

      return acc;
    }, {});

    const order = Object.keys(characters).sort();

    // Reverse if sorting descending
    if (sortDirection === DESCENDING) {
      order.reverse();
    }

    return {
      characters,
      order,
    };
  }, [data, sortKey, sortDirection]);
  const ViewComponent = useMemo(() => getViewComponent(view), [view]);

  const isLoaded = !!(!error && isFetched && data.length);
  const hasNoManga = !totalItems;

  return (
    <QueueDetailsProvider all={true}>
      <SelectProvider items={data}>
        <PageContent>
          <PageToolbar>
            <PageToolbarSection>
              <MangaIndexRefreshMangaButton
                isSelectMode={isSelectMode}
                selectedFilterKey={selectedFilterKey}
              />

              <PageToolbarButton
                label={translate('RssSync')}
                iconName={icons.RSS}
                isSpinning={isRssSyncExecuting}
                isDisabled={hasNoManga}
                onPress={onRssSyncPress}
              />

              <PageToolbarSeparator />

              <MangaIndexSelectModeButton
                label={
                  isSelectMode
                    ? translate('StopSelecting')
                    : translate('SelectManga')
                }
                iconName={isSelectMode ? icons.SERIES_ENDED : icons.CHECK}
                isSelectMode={isSelectMode}
                overflowComponent={MangaIndexSelectModeMenuItem}
                onPress={onSelectModePress}
              />

              <MangaIndexSelectAllButton
                label="SelectAll"
                isSelectMode={isSelectMode}
                overflowComponent={MangaIndexSelectAllMenuItem}
              />

              <PageToolbarSeparator />
              <ParseToolbarButton />
            </PageToolbarSection>

            <PageToolbarSection
              alignContent={align.RIGHT}
              collapseButtons={false}
            >
              {view === 'table' ? (
                <TableOptionsModalWrapper
                  columns={columns}
                  optionsComponent={MangaIndexTableOptions}
                  onTableOptionChange={onTableOptionChange}
                >
                  <PageToolbarButton
                    label={translate('Options')}
                    iconName={icons.TABLE}
                  />
                </TableOptionsModalWrapper>
              ) : (
                <PageToolbarButton
                  label={translate('Options')}
                  iconName={view === 'posters' ? icons.POSTER : icons.OVERVIEW}
                  isDisabled={hasNoManga}
                  onPress={onOptionsPress}
                />
              )}

              <PageToolbarSeparator />

              <MangaIndexViewMenu
                view={view}
                isDisabled={hasNoManga}
                onViewSelect={onViewSelect}
              />

              <MangaIndexSortMenu
                sortKey={sortKey}
                sortDirection={sortDirection}
                isDisabled={hasNoManga}
                onSortSelect={onSortSelect}
              />

              <MangaIndexFilterMenu
                selectedFilterKey={selectedFilterKey}
                filters={filters}
                customFilters={customFilters}
                isDisabled={hasNoManga}
                onFilterSelect={onFilterSelect}
              />
            </PageToolbarSection>
          </PageToolbar>
          <div className={styles.pageContentBodyWrapper}>
            <PageContentBody
              ref={scrollerRef}
              className={styles.contentBody}
              // eslint-disable-next-line @typescript-eslint/ban-ts-comment
              // @ts-ignore
              innerClassName={styles[`${view}InnerContentBody`]}
              scrollPositionKey="seriesIndex"
              onScroll={onScroll}
            >
              {isFetching && !isFetched ? <LoadingIndicator /> : null}

              {!isFetching && !!error ? (
                <Alert kind={kinds.DANGER}>
                  {translate('SeriesLoadError')}
                </Alert>
              ) : null}

              {isLoaded ? (
                <div className={styles.contentBodyContainer}>
                  <ViewComponent
                    scrollerRef={scrollerRef}
                    items={data}
                    sortKey={sortKey}
                    sortDirection={sortDirection}
                    jumpToCharacter={jumpToCharacter}
                    isSelectMode={isSelectMode}
                    isSmallScreen={isSmallScreen}
                  />

                  <MangaIndexFooter />
                </div>
              ) : null}

              {!error && isFetched && !data.length ? (
                <NoManga totalItems={totalItems} />
              ) : null}
            </PageContentBody>
            {isLoaded && !!jumpBarItems.order.length ? (
              <PageJumpBar
                items={jumpBarItems}
                onItemPress={onJumpBarItemPress}
              />
            ) : null}
          </div>

          {isSelectMode ? <MangaIndexSelectFooter /> : null}

          {view === 'posters' ? (
            <MangaIndexPosterOptionsModal
              isOpen={isOptionsModalOpen}
              onModalClose={onOptionsModalClose}
            />
          ) : null}
          {view === 'overview' ? (
            <MangaIndexOverviewOptionsModal
              isOpen={isOptionsModalOpen}
              onModalClose={onOptionsModalClose}
            />
          ) : null}
        </PageContent>
      </SelectProvider>
    </QueueDetailsProvider>
  );
}

export default MangaIndex;
