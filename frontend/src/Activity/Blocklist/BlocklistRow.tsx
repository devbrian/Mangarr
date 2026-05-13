// Sonarr divergence: rewritten as manga-shape MangaBlocklist consumer per
// GH issue #73 (Plan 15-12 follow-up) — see DIVERGENCE.md.
// Role-match analog: frontend/src/Wanted/Missing/MissingRow.tsx (canonical
// manga-shape per-row dispatch precedent — same idiom, different column set).
//
// Pre-fix shape: consumed TV `seriesId` / `quality` / `customFormats` /
// `languages` props that the manga backend never emits. The
// `if (!series) return null` early-return hid every manga row because
// `useSingleSeries(undefined)` returned undefined for every record. The page
// alert path branched on `selectedFilterKey === 'all'` (which it always is for
// manga since the wire shape carries no series filter) to render
// `NoBlocklistItemsManga` — masking the rows-were-rendered-as-null bug.
//
// Post-fix shape: consumes `MangaBlocklist` props directly via spread from
// `Blocklist.tsx`'s `{...item}`. Resolves manga-title link via the hydrated
// `manga` subresource (fallback to `useSingleManga(mangaId)` from the
// `['/manga']` cache). The TV-only `languages` / `quality` / `customFormats`
// columns are dropped from the registry; `translatedLanguage` (manga's BCP-47
// single-string) and `reason` (manga's analog of TV `message`) are added.
//
// Column-key strategy: preserves the legacy TV column keys
// (`series.sortTitle`, `sourceTitle`, `date`, `indexer`, `actions`) so the
// persisted Zustand `blocklist_options` state of upgrade-path users continues
// to work without a localStorage migration; the column labels are flipped in
// `blocklistOptionsStore.ts`.
//
// Phase 8 cleanup: when Tv/ deletes, the column keys can rename to manga.X.
import React, { useCallback, useState } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import LanguageBadge from 'Chapter/LanguageBadge';
import IconButton from 'Components/Link/IconButton';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import { icons, kinds } from 'Helpers/Props';
import MangaTitleLink from 'Manga/MangaTitleLink';
import { useSingleManga } from 'Manga/useManga';
import MangaBlocklist, {
  MangaBlocklistSubresource,
} from 'typings/MangaBlocklist';
import { SelectStateInputProps } from 'typings/props';
import translate from 'Utilities/String/translate';
import BlocklistDetailsModal from './BlocklistDetailsModal';
import { useRemoveBlocklistItem } from './useBlocklist';
import styles from './BlocklistRow.css';

interface BlocklistRowProps {
  id: number;
  mangaId: number;
  chapterIds?: number[];
  sourceTitle?: string;
  sourceKey?: string;
  releaseGuid?: string;
  date: string;
  reason?: string;
  source?: string;
  translatedLanguage?: string;
  manga?: MangaBlocklistSubresource;
  // TV-shape legacy props (kept optional so the wire-layer spread compiles
  // when the user opens an upgrade-path page with the pre-cutover wire shape;
  // they are unused in render — manga has no quality / customFormats /
  // protocol / indexer concept on its blocklist resource).
  indexer?: string;
  protocol?: string;
  message?: string;
  columns: Column[];
}

function BlocklistRow({
  id,
  mangaId,
  sourceTitle,
  translatedLanguage,
  date,
  reason,
  source,
  protocol,
  indexer,
  message,
  manga: hydratedManga,
  columns,
}: BlocklistRowProps) {
  const lookedUpManga = useSingleManga(mangaId);
  const manga = hydratedManga ?? lookedUpManga;

  const { isRemoving, removeBlocklistItem } = useRemoveBlocklistItem(id);
  const [isDetailsModalOpen, setIsDetailsModalOpen] = useState(false);
  const { toggleSelected, useIsSelected } = useSelect<MangaBlocklist>();
  const isSelected = useIsSelected(id);

  const handleSelectedChange = useCallback(
    ({ id, value, shiftKey = false }: SelectStateInputProps) => {
      toggleSelected({ id, isSelected: value, shiftKey });
    },
    [toggleSelected]
  );

  const handleDetailsPress = useCallback(() => {
    setIsDetailsModalOpen(true);
  }, [setIsDetailsModalOpen]);

  const handleDetailsModalClose = useCallback(() => {
    setIsDetailsModalOpen(false);
  }, [setIsDetailsModalOpen]);

  const handleRemovePress = useCallback(() => {
    removeBlocklistItem();
  }, [removeBlocklistItem]);

  return (
    <TableRow data-testid={`manga-blocklist-row-${id}`}>
      <TableSelectCell
        id={id}
        isSelected={isSelected}
        onSelectedChange={handleSelectedChange}
      />

      {columns.map((column) => {
        const { name, isVisible } = column;

        if (!isVisible) {
          return null;
        }

        if (name === 'series.sortTitle') {
          // Phase 18 Plan-05: legacy Zustand key kept; canonical manga-shape
          // testid (`-manga`) exposed.
          return (
            <TableRowCell
              key={name}
              data-testid={`manga-blocklist-row-${id}-manga`}
            >
              {manga ? (
                <MangaTitleLink
                  titleSlug={
                    'titleSlug' in manga
                      ? (manga as { titleSlug?: string }).titleSlug
                      : undefined
                  }
                  title={manga.title ?? sourceTitle ?? ''}
                />
              ) : (
                sourceTitle ?? ''
              )}
            </TableRowCell>
          );
        }

        if (name === 'sourceTitle') {
          return <TableRowCell key={name}>{sourceTitle ?? ''}</TableRowCell>;
        }

        if (name === 'translatedLanguage') {
          return (
            <TableRowCell key={name}>
              <LanguageBadge language={translatedLanguage} />
            </TableRowCell>
          );
        }

        if (name === 'date') {
          return (
            <RelativeDateCell
              key={name}
              date={date}
              data-testid={`manga-blocklist-row-${id}-date`}
            />
          );
        }

        if (name === 'indexer') {
          return (
            <TableRowCell key={name} className={styles.indexer}>
              {indexer ?? ''}
            </TableRowCell>
          );
        }

        if (name === 'reason') {
          return (
            <TableRowCell
              key={name}
              data-testid={`manga-blocklist-row-${id}-reason`}
            >
              {reason ?? message ?? ''}
            </TableRowCell>
          );
        }

        if (name === 'actions') {
          return (
            <TableRowCell
              key={name}
              className={styles.actions}
              data-testid={`manga-blocklist-row-${id}-actions`}
            >
              <IconButton
                name={icons.INFO}
                aria-label={translate('Details')}
                onPress={handleDetailsPress}
                data-testid={`manga-blocklist-row-${id}-details-button`}
              />

              <IconButton
                title={translate('RemoveFromBlocklist')}
                aria-label={translate('RemoveFromBlocklist')}
                name={icons.REMOVE}
                kind={kinds.DANGER}
                isSpinning={isRemoving}
                onPress={handleRemovePress}
                data-testid={`manga-blocklist-row-${id}-remove-button`}
              />
            </TableRowCell>
          );
        }

        return null;
      })}

      <BlocklistDetailsModal
        isOpen={isDetailsModalOpen}
        sourceTitle={sourceTitle ?? ''}
        protocol={(protocol ?? 'unknown') as 'usenet' | 'torrent' | 'unknown'}
        indexer={indexer ?? ''}
        message={reason ?? message}
        source={source}
        onModalClose={handleDetailsModalClose}
      />
    </TableRow>
  );
}

export default BlocklistRow;
