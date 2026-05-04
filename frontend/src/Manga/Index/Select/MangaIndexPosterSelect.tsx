import React, { SyntheticEvent, useCallback } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import styles from './MangaIndexPosterSelect.css';

interface MangaIndexPosterSelectProps {
  mangaId: number;
  // Optional because the manga shape (Plan 07-03 Manga.ts) marks titleSlug
  // optional until the Phase 2 backend gap-fill emits it on every record.
  titleSlug?: string;
}

function MangaIndexPosterSelect({
  mangaId,
  titleSlug,
}: MangaIndexPosterSelectProps) {
  const { toggleSelected, useIsSelected } = useSelect();
  const isSelected = useIsSelected(mangaId);

  const onSelectPress = useCallback(
    (event: SyntheticEvent<HTMLElement, PointerEvent>) => {
      if (event.nativeEvent.ctrlKey || event.nativeEvent.metaKey) {
        window.open(`${window.Sonarr.urlBase}/manga/${titleSlug}`, '_blank');
        return;
      }

      const shiftKey = event.nativeEvent.shiftKey;

      toggleSelected({
        id: mangaId,
        isSelected: !isSelected,
        shiftKey,
      });
    },
    [mangaId, titleSlug, isSelected, toggleSelected]
  );

  return (
    <Link className={styles.checkButton} onPress={onSelectPress}>
      <span className={styles.checkContainer}>
        <Icon
          className={isSelected ? styles.selected : styles.unselected}
          name={isSelected ? icons.CHECK_CIRCLE : icons.CIRCLE_OUTLINE}
          size={20}
        />
      </span>
    </Link>
  );
}

export default MangaIndexPosterSelect;
