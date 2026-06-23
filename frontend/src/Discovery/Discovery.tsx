// Phase 42 Plan 42-05 — Discovery page shell (NEW-in-Mangarr surface).
// Phase 42 Plan 42-07 — grid render + count-only bulk-add modal + per-card
// Exclude/Undo + poolExhausted banner + optimistic drops.
//
// Role-match analog: frontend/src/AddManga/AddNewManga/AddNewManga.tsx (PageContent
// shell + manual-trigger flow). Sketch 001 (winner C) toolbar order:
//   ⚙ Filters … Top [N]  [Search]  [Add]
//
// Search fires useDiscoverySearch (a mutation) so NO /discovery/search request
// fires on mount (D-03 / threat T-42-05-RATE). Results live in volatile mutation
// state; the grid keeps a local `removedIds` set so added/excluded rows drop
// optimistically (D-08) WITHOUT touching ['/manga'] (the results are
// Discovery-local, so there is no SignalR double-removal conflict).
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import NumberInput, { NumberInputChanged } from 'Components/Form/NumberInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import AddTopXModal from 'Discovery/AddTopX/AddTopXModal';
import DiscoveryCard from 'Discovery/DiscoveryCard';
import {
  DiscoveryResult,
  toDiscoverySearchRequest,
} from 'Discovery/DiscoveryModels';
import {
  setDiscoveryOption,
  useDiscoveryOption,
  useDiscoveryOptions,
} from 'Discovery/discoveryOptionsStore';
import FilterDrawer from 'Discovery/FilterDrawer/FilterDrawer';
import {
  deleteDiscoveryExclusion,
  useDiscoveryExclude,
  useDiscoverySearch,
} from 'Discovery/useDiscovery';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './Discovery.css';

interface UndoState {
  mangaBakaId: number;
  exclusionId: number | null;
  title: string;
}

const UNDO_TOAST_SECONDS = 8;

function Discovery() {
  const options = useDiscoveryOptions();
  const topX = useDiscoveryOption('topX');

  const [isFilterDrawerOpen, setIsFilterDrawerOpen] = useState(false);
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  // Optimistic-drop set: ids removed by a successful add (D-08) or an exclude
  // (D-05) since the last Search. Reset whenever a new search lands.
  const [removedIds, setRemovedIds] = useState<Set<number>>(new Set());
  const [undoState, setUndoState] = useState<UndoState | null>(null);

  const { mutate: search, data, isPending } = useDiscoverySearch();
  const { mutateAsync: excludeAsync } = useDiscoveryExclude();

  // A fresh Search result clears the optimistic-drop set + any pending Undo.
  useEffect(() => {
    setRemovedIds(new Set());
    setUndoState(null);
  }, [data]);

  const results = useMemo(() => data?.results ?? [], [data]);
  const visibleResults = useMemo(
    () => results.filter((r) => !removedIds.has(r.mangaBakaId)),
    [results, removedIds]
  );
  const hasResults = visibleResults.length > 0;
  const poolExhausted = Boolean(data && data.found < data.requested);

  const visibleMangaBakaIds = useMemo(
    () => visibleResults.map((r) => r.mangaBakaId),
    [visibleResults]
  );

  const handleTopXChange = useCallback(({ value }: NumberInputChanged) => {
    setDiscoveryOption('topX', value ?? 20);
  }, []);

  const handleToggleFilterDrawer = useCallback(() => {
    setIsFilterDrawerOpen((open) => !open);
  }, []);

  const handleCloseFilterDrawer = useCallback(() => {
    setIsFilterDrawerOpen(false);
  }, []);

  const handleSearchPress = useCallback(() => {
    search(toDiscoverySearchRequest({ ...options, topX }));
  }, [search, options, topX]);

  const handleAddPress = useCallback(() => {
    setIsAddModalOpen(true);
  }, []);

  const handleAddModalClose = useCallback(() => {
    setIsAddModalOpen(false);
  }, []);

  // Drop the just-added rows from the grid optimistically (D-08).
  const handleAdded = useCallback((addedIds: number[]) => {
    setRemovedIds((prev) => {
      const next = new Set(prev);
      addedIds.forEach((id) => next.add(id));
      return next;
    });
  }, []);

  // Per-card Exclude (D-05): drop instantly, POST the GLOBAL ImportListExclusion
  // optimistically, then offer an Undo that DELETEs the freshly-created row.
  const handleExclude = useCallback(
    (result: DiscoveryResult) => {
      const { mangaBakaId, title } = result;

      // (a) instant drop
      setRemovedIds((prev) => new Set(prev).add(mangaBakaId));

      // (b) optimistic POST — capture the created id for Undo
      setUndoState({ mangaBakaId, exclusionId: null, title });
      excludeAsync({ mangaBakaId, title })
        .then((created) => {
          setUndoState((prev) =>
            prev && prev.mangaBakaId === mangaBakaId
              ? { ...prev, exclusionId: created.id }
              : prev
          );
        })
        .catch(() => {
          // The exclude POST failed — re-insert the card and drop the toast so
          // the grid reflects reality (the global exclusion was NOT written).
          setRemovedIds((prev) => {
            const next = new Set(prev);
            next.delete(mangaBakaId);
            return next;
          });
          setUndoState((prev) =>
            prev && prev.mangaBakaId === mangaBakaId ? null : prev
          );
        });
    },
    [excludeAsync]
  );

  const handleUndo = useCallback(() => {
    if (!undoState) {
      return;
    }

    const { mangaBakaId, exclusionId } = undoState;

    // DELETE the exclusion row (if the POST already returned its id) + re-insert
    // the card.
    if (exclusionId != null) {
      deleteDiscoveryExclusion(exclusionId).catch(() => {
        // Swallow — the row may already be gone; the grid re-insert below is the
        // user-visible effect that matters.
      });
    }

    setRemovedIds((prev) => {
      const next = new Set(prev);
      next.delete(mangaBakaId);
      return next;
    });
    setUndoState(null);
  }, [undoState]);

  // Auto-dismiss the Undo toast.
  useEffect(() => {
    if (!undoState) {
      return undefined;
    }
    const timer = setTimeout(
      () => setUndoState(null),
      UNDO_TOAST_SECONDS * 1000
    );
    return () => clearTimeout(timer);
  }, [undoState]);

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

        {!isPending && poolExhausted ? (
          <Alert kind={kinds.WARNING}>
            <span data-testid="discovery-pool-exhausted">
              {translate('DiscoveryPoolExhausted', { found: data?.found ?? 0 })}
            </span>
          </Alert>
        ) : null}

        {!isPending && hasResults ? (
          <div className={styles.grid} data-testid="discovery-grid">
            {visibleResults.map((result) => (
              <DiscoveryCard
                key={result.mangaBakaId}
                result={result}
                onExclude={handleExclude}
              />
            ))}
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

      {undoState ? (
        <div className={styles.undoToast} data-testid="discovery-undo-toast">
          <span className={styles.undoText}>
            {translate('DiscoveryExcluded', { title: undoState.title })}
          </span>

          <Link
            className={styles.undoButton}
            data-testid="discovery-undo-button"
            onPress={handleUndo}
          >
            {translate('Undo')}
          </Link>
        </div>
      ) : null}

      <FilterDrawer
        isOpen={isFilterDrawerOpen}
        onClose={handleCloseFilterDrawer}
      />

      <AddTopXModal
        isOpen={isAddModalOpen}
        mangaBakaIds={visibleMangaBakaIds}
        onAdded={handleAdded}
        onModalClose={handleAddModalClose}
      />
    </PageContent>
  );
}

export default Discovery;
