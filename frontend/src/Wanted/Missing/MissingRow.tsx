// Sonarr divergence: rewritten as manga-shape ChapterRow consumer per
// debug session wanted-missing-zero-rows (GH issue #48) — see DIVERGENCE.md.
// Role-match analog: frontend/src/Manga/Details/ChapterRow.tsx (canonical
// manga-shape per-row dispatch — same idiom, different column set).
//
// Pre-fix shape: reads `series.X` Series subresource the manga
// backend never hydrates (MangaMissingController returns ChapterResource
// with `mangaId` and optional `manga: {id, title}` subresource — never any
// `seriesId`). useSingleSeries(undefined) short-circuits to undefined →
// every row early-returns null → empty tbody despite totalRecords > 0.
// (Sonarr's MissingRow consumes EpisodeResource directly; the manga peer
// must consume ChapterResource directly. Hydrating a Series subresource
// would only get past the first null-gate; downstream cells like
// SeasonEpisodeNumber / EpisodeStatus / EpisodeSearchCell read TV-only
// fields like seasonNumber / episodeFileId / seriesType that have no
// manga analog per PROJECT.md "Volumes/Seasons" + "Scene numbering"
// Out-of-Scope.)
//
// Post-fix shape: consumes Chapter props directly via spread from
// Missing.tsx:332 `<MissingRow {...item} />` over the controller's
// PagingResource<ChapterResource> records. Resolves manga-title link via
// useSingleManga(mangaId) (cached in the React Query `['/manga']` store
// already populated by the sidebar Series list page).
//
// Phase 8 cleanup: collapse with the manga sibling when Tv/ deletes —
// at that point this row is the canonical "Missing" row.
import React, { useCallback } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import Chapter from 'Chapter/Chapter';
import ChapterNumber from 'Chapter/ChapterNumber';
import ChapterSearchCell from 'Chapter/ChapterSearchCell';
import ChapterStatus from 'Chapter/ChapterStatus';
import ChapterTitleLink from 'Chapter/ChapterTitleLink';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import MangaTitleLink from 'Manga/MangaTitleLink';
import { useSingleManga } from 'Manga/useManga';
import { SelectStateInputProps } from 'typings/props';
import styles from './MissingRow.css';

// Field set mirrors `Mangarr.Api.V5.Manga.Chapter.ChapterResource` exactly.
// `columns` is appended by the parent table; the optional `manga` subresource
// is hydrated when the parent fetch passes `?includeSubresources=Manga`
// (currently it does not, so we resolve via useSingleManga(mangaId) below —
// keeps zero added network round-trips because the manga is already cached
// in the `['/manga']` React Query store from the sidebar Series page).
interface MissingRowProps extends Chapter {
  columns: Column[];
}

function MissingRow({
  id,
  mangaId,
  chapterNumber,
  absoluteChapterNumber,
  volumeNumber,
  title,
  chapterType,
  firstReleaseDate,
  monitored,
  hasFile,
  chapterFileId,
  externalId,
  manga: hydratedManga,
  columns,
}: MissingRowProps) {
  // Prefer hydrated subresource when present; fall back to the cached
  // /manga lookup (populated by the sidebar Series page on mount). The
  // hook returns `undefined` while the cache hydrates — we still render
  // the row with the chapter data so the user sees Missing chapters
  // immediately; the title-link cell renders an empty link until the
  // manga lookup resolves (typically <100ms).
  const lookedUpManga = useSingleManga(mangaId);
  const manga = hydratedManga ?? lookedUpManga;

  // Reconstruct a Chapter-shape value for the ChapterStatus cell, which
  // expects a single `chapter` prop. We use the destructured fields
  // rather than re-spreading `...rest` to keep the prop list explicit
  // (and to satisfy ChapterStatus's expected type).
  const chapter: Chapter = {
    id,
    mangaId,
    chapterNumber,
    absoluteChapterNumber,
    volumeNumber,
    title,
    chapterType,
    firstReleaseDate,
    monitored,
    hasFile,
    chapterFileId,
    externalId,
  };

  const { toggleSelected, useIsSelected } = useSelect<Chapter>();
  const isSelected = useIsSelected(id);

  const handleSelectedChange = useCallback(
    ({ id, value, shiftKey = false }: SelectStateInputProps) => {
      toggleSelected({
        id,
        isSelected: value,
        shiftKey,
      });
    },
    [toggleSelected]
  );

  return (
    <TableRow>
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

        // Column keys preserved verbatim from the pre-fix options-store
        // (`series.sortTitle`, `episode`, `episodes.title`,
        // `episodes.airDateUtc`, `episodes.lastSearchTime`, `status`,
        // `actions`) so the existing missingOptionsStore + filter modal
        // continue to work without churn. Phase 8 cleanup: rename the
        // keys to manga.X / chapters.X when the TV peer deletes.
        if (name === 'series.sortTitle') {
          return (
            <TableRowCell key={name}>
              <MangaTitleLink
                titleSlug={manga?.titleSlug}
                title={manga?.title ?? ''}
              />
            </TableRowCell>
          );
        }

        if (name === 'episode') {
          return (
            <TableRowCell key={name} className={styles.episode}>
              <ChapterNumber
                chapterNumber={chapterNumber}
                absoluteChapterNumber={absoluteChapterNumber}
                volumeNumber={volumeNumber}
                showVolumeNumber={volumeNumber != null}
              />
            </TableRowCell>
          );
        }

        if (name === 'episodes.title') {
          return (
            <TableRowCell key={name}>
              <ChapterTitleLink
                chapterId={id}
                mangaId={mangaId}
                chapterTitle={title}
              />
            </TableRowCell>
          );
        }

        if (name === 'episodes.airDateUtc') {
          // Sonarr-mirror: ChapterResource.firstReleaseDate is the manga
          // analog of EpisodeResource.airDateUtc per Phase 16 D-02.
          return <RelativeDateCell key={name} date={firstReleaseDate} />;
        }

        if (name === 'episodes.lastSearchTime') {
          // ChapterResource v1 does not emit lastSearchTime — render
          // empty cell so the column slot is preserved when the user
          // toggles the "Last Searched" column visible. Backfill when
          // the wire shape grows the field.
          return <RelativeDateCell key={name} date={undefined} includeSeconds={true} />;
        }

        if (name === 'status') {
          return (
            <TableRowCell key={name} className={styles.status}>
              <ChapterStatus chapter={chapter} />
            </TableRowCell>
          );
        }

        if (name === 'actions') {
          return (
            <ChapterSearchCell
              key={name}
              chapterId={id}
              mangaId={mangaId}
              chapterTitle={title}
            />
          );
        }

        return null;
      })}
    </TableRow>
  );
}

export default MissingRow;
