import React from 'react';
import Manga from 'Manga/Manga';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';

interface MangaDeleteListStyles {
  pathContainer: string;
  path: string;
  statistics: string;
  deleteFilesMessage: string;
}

interface MangaDeleteListProps {
  manga: Manga[];
  showFileDetails: boolean;
  totalEpisodeFileCount: number;
  totalSizeOnDisk: number;
  styles: MangaDeleteListStyles;
}

function MangaDeleteList({
  manga,
  showFileDetails,
  totalEpisodeFileCount,
  totalSizeOnDisk,
  styles,
}: MangaDeleteListProps) {
  return (
    <>
      <ul>
        {manga.map(({ title, path, statistics = {} }) => {
          // Sonarr divergence: Phase 17.3 D-13/D-14 — destructure
          // chapterFileCount (manga-canonical) instead of episodeFileCount
          // (Manga.ts D-13 trim removed the TV-shape episodeFileCount field
          // from Statistics).
          const { chapterFileCount = 0, sizeOnDisk = 0 } = statistics;

          return (
            <li key={title}>
              <span>{title}</span>

              {showFileDetails ? (
                <span>
                  <span className={styles.pathContainer}>
                    -<span className={styles.path}>{path}</span>
                  </span>

                  {chapterFileCount ? (
                    <span className={styles.statistics}>
                      (
                      {translate('DeleteMangaFolderEpisodeCount', {
                        episodeFileCount: chapterFileCount,
                        size: formatBytes(sizeOnDisk),
                      })}
                      )
                    </span>
                  ) : null}
                </span>
              ) : null}
            </li>
          );
        })}
      </ul>

      {showFileDetails && totalEpisodeFileCount ? (
        <div className={styles.deleteFilesMessage}>
          {translate('DeleteMangaFolderEpisodeCount', {
            episodeFileCount: totalEpisodeFileCount,
            size: formatBytes(totalSizeOnDisk),
          })}
        </div>
      ) : null}
    </>
  );
}

export default MangaDeleteList;
