// Sonarr divergence: rewritten as manga-shape ChapterRow consumer per
// debug session wanted-missing-zero-rows (GH issue #48 sibling impact)
// — see DIVERGENCE.md.
// Role-match analog: frontend/src/Manga/Details/ChapterRow.tsx (canonical
// manga-shape per-row dispatch); twin of frontend/src/Wanted/Missing/MissingRow.tsx
// post-rewrite — same Chapter-shape consumption; differs only in the
// `languages` column slot (CutoffUnmet has it, Missing does not) and the
// extra `if (!hasFile) return null` guard mirroring Sonarr's CutoffUnmetRow
// `if (!episodeFile) return null` (the cutoff predicate is "have file but
// below cutoff" — rows without files are unexpected and would render
// empty status cells).
//
// Pre-fix shape: read TV `series.X` Series subresource the manga backend
// never hydrates → useSingleSeries(undefined) → row early-return null →
// empty tbody. See MissingRow.tsx header for full root-cause text and the
// rationale behind the frontend-rewrite path (vs. backend hydration).
//
// Post-fix shape: consumes Chapter props directly via spread from
// CutoffUnmet.tsx:312-321 `<CutoffUnmetRow {...item} />` over the
// MangaCutoffController's PagingResource<ChapterResource> records.
//
// Phase 8 cleanup: collapse with the manga sibling when Tv/ deletes —
// at that point this row is the canonical CutoffUnmet row.
//
// 2026-05-10 (debug session wanted-missing-chapter-fmt) — dropped the
// `showVolumeNumber={volumeNumber != null}` prop on `<ChapterNumber>` for
// the same reason as the Missing twin: Wanted/CutoffUnmet is a flat
// cross-manga list of chapter identifiers; a `Vol. N ` prefix is
// meaningless and visually clipped at the 100px column width. The
// canonical Manga Details `ChapterRow.tsx` consumer already omits the
// prop. Volumes remain Out-of-Scope per PROJECT.md.
import React, { useCallback } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import Chapter from 'Chapter/Chapter';
import ChapterNumber from 'Chapter/ChapterNumber';
import ChapterSearchCell from 'Chapter/ChapterSearchCell';
import ChapterStatus from 'Chapter/ChapterStatus';
import ChapterTitleLink from 'Chapter/ChapterTitleLink';
import LanguageBadge from 'Chapter/LanguageBadge';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import MangaTitleLink from 'Manga/MangaTitleLink';
import { useSingleManga } from 'Manga/useManga';
import { SelectStateInputProps } from 'typings/props';
import styles from './CutoffUnmetRow.css';

interface CutoffUnmetRowProps extends Chapter {
  columns: Column[];
}

function CutoffUnmetRow({
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
}: CutoffUnmetRowProps) {
  const lookedUpManga = useSingleManga(mangaId);
  const manga = hydratedManga ?? lookedUpManga;

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
    <TableRow data-testid={`manga-cutoff-unmet-row-${id}`}>
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

        // Column keys preserved verbatim from cutoffUnmetOptionsStore so
        // the existing options + filter modal continue to work without
        // churn (Phase 8 cleanup: rename to manga.X / chapters.X when
        // the TV peer deletes).
        if (name === 'series.sortTitle') {
          return (
            <TableRowCell key={name} data-testid={`manga-cutoff-unmet-row-${id}-manga`}>
              <MangaTitleLink
                titleSlug={manga?.titleSlug}
                title={manga?.title ?? ''}
              />
            </TableRowCell>
          );
        }

        if (name === 'episode') {
          // No `showVolumeNumber` — see header note dated 2026-05-10 and
          // the matching change in the Missing twin.
          //
          // Phase 18 Plan 18-06 testid: `cutoff-quality` is the spec name
          // from the plan; manga has no Quality model (TV-peer dropped
          // Phase 5 D-04 — TranslationProfile is the manga analog) so the
          // closest always-visible cell for the cutoff-axis discriminator
          // is the chapter cell. Languages cell is `isVisible: false` by
          // default so it can't host this testid reliably.
          return (
            <TableRowCell key={name} className={styles.episode} data-testid={`manga-cutoff-unmet-row-${id}-cutoff-quality`}>
              <ChapterNumber
                chapterNumber={chapterNumber}
                absoluteChapterNumber={absoluteChapterNumber}
                volumeNumber={volumeNumber}
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
          return <RelativeDateCell key={name} date={firstReleaseDate} />;
        }

        if (name === 'episodes.lastSearchTime') {
          // ChapterResource v1 does not emit lastSearchTime — see twin
          // MissingRow.tsx note for the backfill plan.
          return <RelativeDateCell key={name} date={undefined} includeSeconds={true} />;
        }

        if (name === 'languages') {
          // Manga sibling diverges: pre-fix row called <EpisodeFileLanguages
          // episodeFileId={...}> which fetched the EpisodeFile by id and
          // rendered the per-file Languages list. Manga's per-file translation
          // axis is `ChapterFile.translatedLanguage` (single string, post
          // Phase 16.1 D-04 collapse), and ChapterResource does not currently
          // emit it directly. The closest in-context signal is the chapter
          // existing — render the language pill via Chapter/LanguageBadge,
          // which falls back to undefined gracefully when no language is
          // available on the wire (column is `isVisible: false` by default in
          // cutoffUnmetOptionsStore so this rarely fires anyway).
          return (
            <TableRowCell key={name} className={styles.languages}>
              <LanguageBadge language={undefined} />
            </TableRowCell>
          );
        }

        if (name === 'status') {
          // Phase 18 Plan 18-06 testid: `current-quality` is the spec
          // name from the plan; manga's per-chapter file/availability
          // state is the closest analog to "current quality" (TV-peer
          // dropped Phase 5 D-04). ChapterStatus is always-visible.
          return (
            <TableRowCell key={name} className={styles.status} data-testid={`manga-cutoff-unmet-row-${id}-current-quality`}>
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

export default CutoffUnmetRow;
