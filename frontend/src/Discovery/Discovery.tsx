// Phase 42 Plan 42-05 — Discovery page shell (NEW-in-Mangarr surface).
// Role-match analog: frontend/src/AddManga/AddNewManga/AddNewManga.tsx (PageContent
// shell + manual-trigger flow). Sketch 001 (winner C) toolbar order:
//   ⚙ Filters … Top [N]  [Search]  [Add]
//
// This plan ships the SHELL only: the filter drawer (42-06) and the bulk-add modal
// (42-07) compose onto it. Search is wired to useDiscoverySearch (a mutation), so
// NO /discovery/search request fires on mount (D-03 / threat T-42-05-RATE) — only
// when the Search button calls mutate(). Until results exist the grid shows the
// "Set filters and Search" empty state. Filters + Top-X persist via
// discoveryOptionsStore (D-09); results live in volatile mutation state.
import React, { useCallback, useState } from 'react';
import NumberInput, { NumberInputChanged } from 'Components/Form/NumberInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import {
  setDiscoveryOption,
  useDiscoveryOption,
  useDiscoveryOptions,
} from 'Discovery/discoveryOptionsStore';
import { useDiscoverySearch } from 'Discovery/useDiscovery';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './Discovery.css';

function Discovery() {
  const options = useDiscoveryOptions();
  const topX = useDiscoveryOption('topX');

  // Filter drawer ships in 42-06 — wire the local open/close state now so the
  // toolbar Filters button is functional (placeholder body until 42-06 lands).
  const [isFilterDrawerOpen, setIsFilterDrawerOpen] = useState(false);

  const { mutate: search, data, isPending } = useDiscoverySearch();

  const results = data?.results ?? [];
  const hasResults = results.length > 0;

  const handleTopXChange = useCallback(({ value }: NumberInputChanged) => {
    setDiscoveryOption('topX', value ?? 20);
  }, []);

  const handleToggleFilterDrawer = useCallback(() => {
    setIsFilterDrawerOpen((open) => !open);
  }, []);

  const handleSearchPress = useCallback(() => {
    search({ ...options, topX });
  }, [search, options, topX]);

  const handleAddPress = useCallback(() => {
    // Bulk-add modal ships in 42-07.
  }, []);

  return (
    <PageContent title={translate('Discovery')}>
      <PageContentBody>
        <div className={styles.toolbar} data-testid="discovery-page">
          <div className={styles.toolbarLeft}>
            <Button
              className={styles.filterButton}
              data-testid="discovery-filters-button"
              aria-expanded={isFilterDrawerOpen}
              onPress={handleToggleFilterDrawer}
            >
              <Icon name={icons.FILTER} size={15} />
              {translate('Filters')}
            </Button>
          </div>

          <div className={styles.toolbarRight}>
            <span className={styles.topLabel}>
              {translate('DiscoveryTopX')}
            </span>

            <NumberInput
              className={styles.topInput}
              name="topX"
              value={topX}
              min={1}
              max={100}
              data-testid="discovery-topx-input"
              onChange={handleTopXChange}
            />

            <Button
              kind={kinds.SUCCESS}
              data-testid="discovery-search-button"
              isDisabled={isPending}
              onPress={handleSearchPress}
            >
              <Icon name={icons.SEARCH} size={15} />
              {translate('Search')}
            </Button>

            <Button
              kind={kinds.PRIMARY}
              data-testid="discovery-add-button"
              isDisabled={!hasResults}
              onPress={handleAddPress}
            >
              <Icon name={icons.ADD} size={15} />
              {translate('Add')}
            </Button>
          </div>
        </div>

        {isPending ? <LoadingIndicator /> : null}

        {!isPending && hasResults ? (
          <div className={styles.grid} data-testid="discovery-grid">
            {/* Result cards render in 42-07. */}
          </div>
        ) : null}

        {!isPending && !hasResults ? (
          <div
            className={styles.emptyState}
            data-testid="discovery-empty-state"
          >
            {translate('DiscoverySetFiltersAndSearch')}
          </div>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default Discovery;
