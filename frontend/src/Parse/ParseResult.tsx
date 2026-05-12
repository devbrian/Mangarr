// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// Episode/EpisodeFormats no-op stub import DROPPED (Phase 15 Plan 15-12 STUB
// component returning null with zero render output); CustomFormats render
// site below collapsed to the count text. Series/SeriesTitleLink rewritten
// to Manga/MangaTitleLink peer (routes to /manga/${titleSlug}).
import React from 'react';
import FieldSet from 'Components/FieldSet';
import MangaTitleLink from 'Manga/MangaTitleLink';
import translate from 'Utilities/String/translate';
import { ParseModel } from './ParseModel';
import ParseResultItem from './ParseResultItem';
import styles from './ParseResult.css';

interface ParseResultProps {
  item: ParseModel;
}

function ParseResult(props: ParseResultProps) {
  const { item } = props;
  const {
    customFormats,
    customFormatScore,
    episodes,
    languages,
    parsedEpisodeInfo,
    series,
  } = item;

  const {
    releaseTitle,
    seriesTitle,
    seriesTitleInfo,
    releaseGroup,
    releaseHash,
    seasonNumber,
    episodeNumbers,
    absoluteEpisodeNumbers,
    special,
    fullSeason,
    isMultiSeason,
    isPartialSeason,
    isDaily,
    airDate,
    quality,
  } = parsedEpisodeInfo;

  const finalLanguages = languages ?? parsedEpisodeInfo.languages;

  return (
    <div>
      <FieldSet legend={translate('Release')}>
        <ParseResultItem
          title={translate('ReleaseTitle')}
          data={releaseTitle}
        />

        <ParseResultItem title={translate('MangaTitle')} data={seriesTitle} />

        <ParseResultItem
          title={translate('Year')}
          data={seriesTitleInfo.year > 0 ? seriesTitleInfo.year : '-'}
        />

        <ParseResultItem
          title={translate('AllTitles')}
          data={
            seriesTitleInfo.allTitles?.length > 0
              ? seriesTitleInfo.allTitles.join(', ')
              : '-'
          }
        />

        <ParseResultItem
          title={translate('ReleaseGroup')}
          data={releaseGroup ?? '-'}
        />

        <ParseResultItem
          title={translate('ReleaseHash')}
          data={releaseHash ? releaseHash : '-'}
        />
      </FieldSet>

      <FieldSet legend={translate('EpisodeInfo')}>
        <div className={styles.container}>
          <div className={styles.column}>
            <ParseResultItem
              title={translate('SeasonNumber')}
              data={
                seasonNumber === 0 && absoluteEpisodeNumbers.length
                  ? '-'
                  : seasonNumber
              }
            />

            <ParseResultItem
              title={translate('EpisodeNumbers')}
              data={episodeNumbers.join(', ') || '-'}
            />

            <ParseResultItem
              title={translate('AbsoluteEpisodeNumbers')}
              data={
                absoluteEpisodeNumbers.length
                  ? absoluteEpisodeNumbers.join(', ')
                  : '-'
              }
            />

            <ParseResultItem
              title={translate('Daily')}
              data={isDaily ? 'True' : 'False'}
            />

            <ParseResultItem
              title={translate('AirDate')}
              data={airDate ?? '-'}
            />
          </div>

          <div className={styles.column}>
            <ParseResultItem
              title={translate('Special')}
              data={special ? translate('True') : translate('False')}
            />

            <ParseResultItem
              title={translate('FullSeason')}
              data={fullSeason ? translate('True') : translate('False')}
            />

            <ParseResultItem
              title={translate('MultiSeason')}
              data={isMultiSeason ? translate('True') : translate('False')}
            />

            <ParseResultItem
              title={translate('PartialSeason')}
              data={isPartialSeason ? translate('True') : translate('False')}
            />
          </div>
        </div>
      </FieldSet>

      <FieldSet legend={translate('Quality')}>
        <div className={styles.container}>
          <div className={styles.column}>
            <ParseResultItem
              title={translate('Quality')}
              data={quality.quality.name}
            />
            <ParseResultItem
              title={translate('Proper')}
              data={
                quality.revision.version > 1 && !quality.revision.isRepack
                  ? translate('True')
                  : '-'
              }
            />

            <ParseResultItem
              title={translate('Repack')}
              data={quality.revision.isRepack ? translate('True') : '-'}
            />
          </div>

          <div className={styles.column}>
            <ParseResultItem
              title={translate('Version')}
              data={
                quality.revision.version > 1 ? quality.revision.version : '-'
              }
            />

            <ParseResultItem
              title={translate('Real')}
              data={quality.revision.real ? translate('True') : '-'}
            />
          </div>
        </div>
      </FieldSet>

      <FieldSet legend={translate('Languages')}>
        <ParseResultItem
          title={translate('Languages')}
          data={finalLanguages.map((l) => l.name).join(', ')}
        />
      </FieldSet>

      <FieldSet legend={translate('Details')}>
        <ParseResultItem
          title={translate('MatchedToSeries')}
          data={
            series ? (
              <MangaTitleLink
                titleSlug={series.titleSlug}
                title={series.title}
              />
            ) : (
              '-'
            )
          }
        />

        <ParseResultItem
          title={translate('MatchedToSeason')}
          data={
            // Sonarr divergence: Phase 17.3 Plan 17.3-13b — Episode/Episode
            // rewritten to Chapter/Chapter; Chapter has no seasonNumber
            // (manga has no seasons per DOMAIN-02). Backend Parse controller
            // still emits seasonNumber at runtime for the TV-shape branch;
            // cast through Chapter & { seasonNumber?: number } preserves the
            // display path until the parser is forked for chapter-mode
            // (tracked for v1.x).
            episodes.length
              ? (episodes[0] as typeof episodes[0] & { seasonNumber?: number }).seasonNumber ?? '-'
              : '-'
          }
        />

        <ParseResultItem
          title={translate('MatchedToEpisodes')}
          data={
            episodes.length ? (
              <div>
                {episodes.map(
                  // Sonarr divergence: Phase 17.3 D-13/D-14 — dropped
                  // `series?.seriesType === 'anime' && e.absoluteEpisodeNumber`
                  // branch (manga has no anime-format; seriesType removed from
                  // Manga.ts per D-13).
                  // Sonarr divergence: Phase 17.3 Plan 17.3-13b —
                  // Episode/Episode rewritten to Chapter/Chapter; cast
                  // preserves runtime episodeNumber emission until parser is
                  // forked.
                  (e: typeof episodes[0] & { episodeNumber?: number }) => {
                    return (
                      <div key={e.id}>
                        {e.episodeNumber}
                        {` - ${e.title}`}
                      </div>
                    );
                  }
                )}
              </div>
            ) : (
              '-'
            )
          }
        />

        {/* Sonarr divergence: Phase 17.3 Plan 17.3-13b — EpisodeFormats no-op
            stub dropped; show CustomFormats count text directly. */}
        <ParseResultItem
          title={translate('CustomFormats')}
          data={customFormats?.length ? `${customFormats.length}` : '-'}
        />

        <ParseResultItem
          title={translate('CustomFormatScore')}
          data={customFormatScore}
        />
      </FieldSet>
    </div>
  );
}

export default ParseResult;
