// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Components/Page/Header/SeriesSearchResult.tsx
// (the TV original was deleted to a stub in Phase 15 Plan 15-12).
//
// Manga sibling diverges from SeriesSearchResult:
//   * Renders MangaPoster (not SeriesPoster).
//   * Shows manga metadata IDs (MangaDexId / AniListId / MalId) when the fuse
//     match keyed off one of them, instead of tvdbId / tvMazeId / imdbId / tmdbId.
import React from 'react';
import Label from 'Components/Label';
import { kinds } from 'Helpers/Props';
import MangaPoster from 'Manga/MangaPoster';
import type { Tag } from 'Tags/useTags';
import type { SuggestedManga } from './MangaSearchInput';
import styles from './MangaSearchResult.css';

interface Match {
  key: string;
  refIndex: number;
}

interface MangaSearchResultProps extends SuggestedManga {
  match: Match;
}

function MangaSearchResult(props: MangaSearchResultProps) {
  const {
    match,
    title,
    images,
    alternateTitles = [],
    mangaDexId,
    aniListId,
    malId,
    tags,
  } = props;

  let alternateTitle = null;
  let tag: Tag | null = null;

  if (match.key === 'alternateTitles.title') {
    alternateTitle = alternateTitles[match.refIndex];
  } else if (match.key === 'tags.label') {
    tag = tags[match.refIndex];
  }

  return (
    <div className={styles.result}>
      <MangaPoster
        className={styles.poster}
        images={images}
        size={250}
        lazy={false}
        overflow={true}
        title={title}
      />

      <div className={styles.titles}>
        <div className={styles.title}>{title}</div>

        {alternateTitle ? (
          <div className={styles.alternateTitle}>{alternateTitle.title}</div>
        ) : null}

        {match.key === 'mangaDexId' && mangaDexId ? (
          <div className={styles.alternateTitle}>MangaDexId: {mangaDexId}</div>
        ) : null}

        {match.key === 'aniListId' && aniListId ? (
          <div className={styles.alternateTitle}>AniListId: {aniListId}</div>
        ) : null}

        {match.key === 'malId' && malId ? (
          <div className={styles.alternateTitle}>MalId: {malId}</div>
        ) : null}

        {tag ? (
          <div className={styles.tagContainer}>
            <Label key={tag.id} kind={kinds.INFO}>
              {tag.label}
            </Label>
          </div>
        ) : null}
      </div>
    </div>
  );
}

export default MangaSearchResult;
