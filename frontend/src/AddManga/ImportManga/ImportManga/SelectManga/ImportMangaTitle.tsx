// Sonarr divergence: NEW manga sibling per debug-add-import-ui-mismatch fix
// (2026-05-19). Role-match analog:
// frontend/src/AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesTitle.tsx.
//
// Manga sibling preserves: title + (year) + provider label + "Existing"
// warning chip visual layout.
// Manga sibling diverges:
//   - "network" (TV channel) -> "metadataSource" label (MangaDex / AniList / MAL)
//   - Year fallback chain: year ?? publicationYear (Mangarr exposes both)
//
// Phase 8 cleanup: collapse with ImportSeriesTitle when AddSeries/ deletes.
import React from 'react';
import Label from 'Components/Label';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './ImportMangaTitle.css';

interface ImportMangaTitleProps {
  title: string;
  year?: number;
  metadataSource?: string;
  isExistingManga: boolean;
}

function ImportMangaTitle({
  title,
  year,
  metadataSource,
  isExistingManga,
}: ImportMangaTitleProps) {
  return (
    <div className={styles.titleContainer}>
      <div className={styles.title}>{title}</div>

      {year && year > 0 && !title.includes(String(year)) ? (
        <span className={styles.year}>({year})</span>
      ) : null}

      {metadataSource ? <Label>{metadataSource}</Label> : null}

      {isExistingManga ? (
        <Label kind={kinds.WARNING}>{translate('Existing')}</Label>
      ) : null}
    </div>
  );
}

export default ImportMangaTitle;
