import classNames from 'classnames';
import React from 'react';
import { ColorImpairedConsumer } from 'App/ColorImpairedContext';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import useManga from 'Manga/useManga';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './MangaIndexFooter.css';

export default function MangaIndexFooter() {
  const { data: manga } = useManga();
  const count = manga.length;
  // CR-02 fix: read manga-shape statistics fields (chapterCount /
  // chapterFileCount) instead of the never-populated TV-shape carry-overs
  // (episodeCount / episodeFileCount). See Manga.ts Statistics interface
  // — episode* fields are explicitly typed as never-populated for manga.
  let chapters = 0;
  let chapterFiles = 0;
  let ended = 0;
  let continuing = 0;
  let monitored = 0;
  let totalFileSize = 0;

  manga.forEach((s) => {
    const {
      statistics = { chapterCount: 0, chapterFileCount: 0, sizeOnDisk: 0 },
    } = s;

    const {
      chapterCount = 0,
      chapterFileCount = 0,
      sizeOnDisk = 0,
    } = statistics;

    chapters += chapterCount;
    chapterFiles += chapterFileCount;

    // Manga divergence: 'ended' bucket maps to the manga 'completed' /
    // 'cancelled' terminal statuses; 'continuing' bucket is everything else.
    // Phase 8 cleanup: re-key the legend itself away from TV terminology.
    if (s.status === 'completed' || s.status === 'cancelled') {
      ended++;
    } else {
      continuing++;
    }

    if (s.monitored) {
      monitored++;
    }

    totalFileSize += sizeOnDisk;
  });

  return (
    <ColorImpairedConsumer>
      {(enableColorImpairedMode) => {
        return (
          <div className={styles.footer}>
            <div>
              <div className={styles.legendItem}>
                <div
                  className={classNames(
                    styles.continuing,
                    enableColorImpairedMode && 'colorImpaired'
                  )}
                />
                <div>{translate('MangaIndexFooterContinuing')}</div>
              </div>

              <div className={styles.legendItem}>
                <div
                  className={classNames(
                    styles.ended,
                    enableColorImpairedMode && 'colorImpaired'
                  )}
                />
                <div>{translate('MangaIndexFooterEnded')}</div>
              </div>

              <div className={styles.legendItem}>
                <div
                  className={classNames(
                    styles.missingMonitored,
                    enableColorImpairedMode && 'colorImpaired'
                  )}
                />
                <div>{translate('MangaIndexFooterMissingMonitored')}</div>
              </div>

              <div className={styles.legendItem}>
                <div
                  className={classNames(
                    styles.missingUnmonitored,
                    enableColorImpairedMode && 'colorImpaired'
                  )}
                />
                <div>{translate('MangaIndexFooterMissingUnmonitored')}</div>
              </div>

              <div className={styles.legendItem}>
                <div
                  className={classNames(
                    styles.downloading,
                    enableColorImpairedMode && 'colorImpaired'
                  )}
                />
                <div>{translate('MangaIndexFooterDownloading')}</div>
              </div>
            </div>

            <div className={styles.statistics}>
              <DescriptionList>
                <DescriptionListItem title={translate('Manga')} data={count} />

                <DescriptionListItem title={translate('Ended')} data={ended} />

                <DescriptionListItem
                  title={translate('Continuing')}
                  data={continuing}
                />
              </DescriptionList>

              <DescriptionList>
                <DescriptionListItem
                  title={translate('Monitored')}
                  data={monitored}
                />

                <DescriptionListItem
                  title={translate('Unmonitored')}
                  data={count - monitored}
                />
              </DescriptionList>

              <DescriptionList>
                <DescriptionListItem
                  title={translate('Chapters')}
                  data={chapters}
                />

                <DescriptionListItem
                  title={translate('Files')}
                  data={chapterFiles}
                />
              </DescriptionList>

              <DescriptionList>
                <DescriptionListItem
                  title={translate('TotalFileSize')}
                  data={formatBytes(totalFileSize)}
                />
              </DescriptionList>
            </div>
          </div>
        );
      }}
    </ColorImpairedConsumer>
  );
}
