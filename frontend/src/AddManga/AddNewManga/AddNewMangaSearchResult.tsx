// Sonarr divergence: NEW manga sibling per Phase 7 D-04 — see DIVERGENCE.md.
// Role-match analog: frontend/src/AddSeries/AddNewSeries/AddNewSeriesSearchResult.tsx.
//
// Manga sibling preserves: result-card poster + content layout, alreadyInLibrary
// icon, on-press opens AddNewMangaModal pattern.
// Manga sibling diverges from AddNewSeriesSearchResult:
//   * Reads AddMangaResult shape (title / year / mangaDexId / aniListId / malId
//     / status / overview / images) — NOT AddSeries (no tvdbId / network /
//     ratings.value).
//   * "Already in library" check matches manga by mangaDexId / aniListId /
//     malId (not tvdbId).
//   * External link points to MangaDex / AniList / MAL when the corresponding
//     ID is set; falls back to MangaDex search when none.
//   * No Network label, no HeartRating block (UI-SPEC §AddManga visual
//     hierarchy lists cover, title, status pill, year+chapter count — no
//     ratings primary anchor for v1).
//   * Existing-manga link routes to /manga/{titleSlug ?? id}.
//
// Phase 8 cleanup: collapse with AddNewSeriesSearchResult when AddSeries/ deletes.
import React, { useCallback, useMemo, useState } from 'react';
import { AddMangaResult } from 'AddManga/AddManga';
import { useAppDimension } from 'App/appStore';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import MetadataAttribution from 'Components/MetadataAttribution';
import { icons, kinds, sizes } from 'Helpers/Props';
import useManga from 'Manga/useManga';
import MangaPoster from 'Manga/MangaPoster';
import translate from 'Utilities/String/translate';
import AddNewMangaModal from './AddNewMangaModal';
import styles from './AddNewMangaSearchResult.css';

interface AddNewMangaSearchResultProps {
  manga: AddMangaResult;
}

function AddNewMangaSearchResult({ manga }: AddNewMangaSearchResultProps) {
  const {
    title,
    titleSlug,
    year,
    status,
    overview,
    images,
    primaryAuthor,
    totalChapterCount,
    mangaDexId,
    aniListId,
    malId,
    isExcluded,
  } = manga;

  // Determine whether this lookup row matches a manga already in the library
  // (Phase 2 MangaResource baseline ships singular mangaDexId / aniListId /
  // malId; we match on whichever ID is populated on either side).
  const { data: libraryManga } = useManga();
  const existingManga = useMemo(() => {
    return libraryManga.find((m) => {
      if (mangaDexId && m.mangaDexId === mangaDexId) {
        return true;
      }
      if (aniListId != null && m.aniListId === aniListId) {
        return true;
      }
      if (malId != null && m.malId === malId) {
        return true;
      }
      return false;
    });
  }, [libraryManga, mangaDexId, aniListId, malId]);
  const isExistingManga = Boolean(existingManga);
  const existingTitleSlug = existingManga?.titleSlug ?? existingManga?.id;

  const isSmallScreen = useAppDimension('isSmallScreen');
  const [isNewAddMangaModalOpen, setIsNewAddMangaModalOpen] = useState(false);

  const handlePress = useCallback(() => {
    setIsNewAddMangaModalOpen(true);
  }, []);

  const handleAddMangaModalClose = useCallback(() => {
    setIsNewAddMangaModalOpen(false);
  }, []);

  const handleExternalLinkPress = useCallback(
    (event: React.SyntheticEvent) => {
      event.stopPropagation();
    },
    []
  );

  const externalLink = useMemo(() => {
    if (mangaDexId) {
      return {
        url: `https://mangadex.org/title/${mangaDexId}`,
        label: 'MangaDex',
      };
    }
    if (aniListId != null) {
      return {
        url: `https://anilist.co/manga/${aniListId}`,
        label: 'AniList',
      };
    }
    if (malId != null) {
      return {
        url: `https://myanimelist.net/manga/${malId}`,
        label: 'MyAnimeList',
      };
    }
    return null;
  }, [mangaDexId, aniListId, malId]);

  const linkProps = isExistingManga
    ? {
        to: `/manga/${existingTitleSlug ?? titleSlug ?? ''}`,
      }
    : { onPress: handlePress };

  return (
    <div className={styles.searchResult}>
      <Link
        className={styles.underlay}
        aria-label={
          isExistingManga ? title : translate('AddMangaWithTitle', { title })
        }
        {...linkProps}
      />

      <div className={styles.overlay}>
        {isSmallScreen ? null : (
          <MangaPoster
            className={styles.poster}
            images={images}
            size={250}
            overflow={true}
            lazy={false}
            title={title}
          />
        )}

        <div className={styles.content}>
          <div className={styles.titleRow}>
            <div className={styles.titleContainer}>
              <div className={styles.title}>
                {title}

                {!title.includes(String(year)) && year ? (
                  <span className={styles.year}>({year})</span>
                ) : null}
              </div>
            </div>

            <div className={styles.icons}>
              {isExistingManga ? (
                <Icon
                  className={styles.alreadyExistsIcon}
                  name={icons.CHECK_CIRCLE}
                  size={36}
                  title={translate('AlreadyInYourLibrary')}
                />
              ) : null}

              {isExcluded ? (
                <Icon
                  className={styles.excludedIcon}
                  name={icons.DANGER}
                  size={36}
                  title={translate('MangaInImportListExclusions')}
                />
              ) : null}

              {externalLink ? (
                <Link
                  className={styles.externalLink}
                  to={externalLink.url}
                  aria-label={translate('ViewMangaOnSource', {
                    title,
                    source: externalLink.label,
                  })}
                  onPress={handleExternalLinkPress}
                >
                  <Icon
                    className={styles.externalLinkIcon}
                    name={icons.EXTERNAL_LINK}
                    size={28}
                    aria-hidden={true}
                  />
                </Link>
              ) : null}
            </div>
          </div>

          <div>
            {primaryAuthor ? (
              <Label size={sizes.LARGE}>
                <Icon name={icons.PROFILE} size={13} />

                <span className={styles.originalLanguageName}>
                  {primaryAuthor}
                </span>
              </Label>
            ) : null}

            {totalChapterCount ? (
              <Label size={sizes.LARGE}>
                {translate('CountChapters', { count: totalChapterCount })}
              </Label>
            ) : null}

            {status === 'ongoing' ? (
              <Label kind={kinds.INFO} size={sizes.LARGE}>
                {translate('Ongoing')}
              </Label>
            ) : null}

            {status === 'completed' ? (
              <Label kind={kinds.SUCCESS} size={sizes.LARGE}>
                {translate('Completed')}
              </Label>
            ) : null}

            {status === 'hiatus' ? (
              <Label kind={kinds.WARNING} size={sizes.LARGE}>
                {translate('Hiatus')}
              </Label>
            ) : null}

            {status === 'cancelled' ? (
              <Label kind={kinds.DANGER} size={sizes.LARGE}>
                {translate('Cancelled')}
              </Label>
            ) : null}
          </div>

          {overview ? (
            <div className={styles.overview}>{overview}</div>
          ) : null}

          <MetadataAttribution />
        </div>
      </div>

      <AddNewMangaModal
        isOpen={isNewAddMangaModalOpen && !isExistingManga}
        manga={manga}
        onModalClose={handleAddMangaModalClose}
      />
    </div>
  );
}

export default AddNewMangaSearchResult;
