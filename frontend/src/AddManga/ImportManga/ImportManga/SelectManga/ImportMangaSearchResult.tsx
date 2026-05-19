// Sonarr divergence: NEW manga sibling per debug-add-import-ui-mismatch fix
// (2026-05-19). Role-match analog:
// frontend/src/AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesSearchResult.tsx.
//
// Manga sibling preserves: clickable card + MetadataSource link icon pattern.
// Manga sibling diverges:
//   - mangaDexId (string) replaces tvdbId (number) as the row identifier;
//     external link target is https://mangadex.org/title/{mangaDexId}.
//   - useExistingManga(mangaDexId) replaces useExistingSeries(tvdbId).
//
// Phase 8 cleanup: collapse with ImportSeriesSearchResult when AddSeries/
// deletes.
import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import useExistingManga from 'Manga/useExistingManga';
import ImportMangaTitle from './ImportMangaTitle';
import styles from './ImportMangaSearchResult.css';

interface ImportMangaSearchResultProps {
  mangaDexId?: string;
  title: string;
  year?: number;
  metadataSource?: string;
  onPress: (mangaDexId: string | undefined) => void;
}

function ImportMangaSearchResult({
  mangaDexId,
  title,
  year,
  metadataSource,
  onPress,
}: ImportMangaSearchResultProps) {
  const isExistingManga = useExistingManga(mangaDexId);

  const handlePress = useCallback(() => {
    onPress(mangaDexId);
  }, [mangaDexId, onPress]);

  return (
    <div className={styles.container}>
      <Link className={styles.manga} onPress={handlePress}>
        <ImportMangaTitle
          title={title}
          year={year}
          metadataSource={metadataSource}
          isExistingManga={isExistingManga}
        />
      </Link>

      {mangaDexId ? (
        <Link
          className={styles.mangaDexLink}
          to={`https://mangadex.org/title/${mangaDexId}`}
        >
          <Icon
            className={styles.mangaDexLinkIcon}
            name={icons.EXTERNAL_LINK}
            size={16}
          />
        </Link>
      ) : null}
    </div>
  );
}

export default ImportMangaSearchResult;
