// Sonarr divergence: NEW manga sibling per Phase 7 D-04 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Episode/EpisodeSearchCell.tsx (Episode
// shape — fires CommandNames.EpisodeSearch + opens InteractiveSearch modal).
//
// Per Open Question 3 lean (RESEARCH): provide BOTH affordances —
//   * Auto search button → POST /api/v5/chapter/{id}/search (Plan 07-01;
//     enqueues a ChapterSearchCommand which runs the auto pipeline).
//   * Interactive search button → opens ChapterDetailsModal which renders
//     <InteractiveSearch type="chapter" searchPayload={{ chapterId }} />.
//
// Manga sibling preserves: TableRowCell wrapper + IconButton + SpinnerIconButton.
// Manga sibling diverges from EpisodeSearchCell:
//   * Auto-search dispatches via useApiMutation against the Plan 07-01
//     backend POST /api/v5/chapter/{id}/search (returns 202 Accepted +
//     command id). Episode equivalent uses the executeCommand bridge to the
//     Sonarr command-queue side; manga sibling skips that bridge because
//     Phase 6 D-12 makes ChapterSearchCommand command-queue-based and the
//     backend endpoint already enqueues.
//   * No EpisodeEntity discriminator; opens the streamlined v1
//     ChapterDetailsModal (search-first).
//   * Accepts an optional `className` prop forwarded to TableRowCell so the
//     parent ChapterRow can apply the `.actions` cell style (fixed width +
//     white-space: nowrap to prevent the two inline-block buttons from
//     stacking vertically). Debug session: manga-details-buttons-tvdb.md.
//
// Phase 8 cleanup: collapse with EpisodeSearchCell when Tv/ deletes.
import React, { useCallback, useState } from 'react';
import IconButton from 'Components/Link/IconButton';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ChapterDetailsModal from './ChapterDetailsModal';

export interface ChapterSearchCellProps {
  chapterId: number;
  mangaId: number;
  chapterTitle?: string;
  className?: string;
}

function ChapterSearchCell({
  chapterId,
  mangaId,
  chapterTitle,
  className,
}: ChapterSearchCellProps) {
  const [isModalOpen, setIsModalOpen] = useState(false);

  // POST /api/v5/chapter/{id}/search — Plan 07-01 endpoint enqueues a
  // ChapterSearchCommand and returns 202 Accepted + command id. Backend
  // dedups same-payload commands per IManageCommandQueue.Push (T-07-04 from
  // Plan 07-01 mitigation).
  const { mutate: triggerAutoSearch, isPending: isSearching } = useApiMutation<
    unknown,
    void
  >({
    path: `/chapter/${chapterId}/search`,
    method: 'POST',
  });

  const handleAutoSearchPress = useCallback(() => {
    triggerAutoSearch();
  }, [triggerAutoSearch]);

  const handleInteractiveSearchPress = useCallback(() => {
    setIsModalOpen(true);
  }, []);

  const handleModalClose = useCallback(() => {
    setIsModalOpen(false);
  }, []);

  return (
    <TableRowCell className={className}>
      <SpinnerIconButton
        name={icons.SEARCH}
        isSpinning={isSearching}
        title={translate('AutomaticSearch')}
        data-testid={`chapter-row-${chapterId}-search-button`}
        onPress={handleAutoSearchPress}
      />

      <IconButton
        name={icons.INTERACTIVE}
        title={translate('ManualSearch')}
        aria-label={translate('ManualSearch')}
        onPress={handleInteractiveSearchPress}
      />

      <ChapterDetailsModal
        isOpen={isModalOpen}
        chapterId={chapterId}
        mangaId={mangaId}
        chapterTitle={chapterTitle}
        onModalClose={handleModalClose}
      />
    </TableRowCell>
  );
}

export default ChapterSearchCell;
