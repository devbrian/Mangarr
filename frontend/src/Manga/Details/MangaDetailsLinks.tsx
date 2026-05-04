// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/SeriesDetailsLinks.tsx
// (TVDB / TVMaze / IMDB / TMDB → MangaDex / AniList / MyAnimeList).
//
// Manga sibling preserves: link-block render + ClipboardButton + Label kinds.
// Manga sibling diverges from SeriesDetailsLinks:
//   * External-source set replaced — TVDB/TVMaze/IMDB/TMDB drop, MangaDex/
//     AniList/MAL surface. URLs sourced from each provider's canonical
//     manga-detail path:
//       MangaDex   → https://mangadex.org/title/{uuid}
//       AniList    → https://anilist.co/manga/{id}
//       MyAnimeList → https://myanimelist.net/manga/{id}
//   * IDs read from the Phase 2 baseline singular fields (mangaDexId /
//     aniListId / malId) which Manga.ts carries as optional. Forward-looking
//     plural arrays (aniListIds[] / malIds[]) are NOT used here — when a
//     manga is 1:1 across sources (the v1 assumption per Phase 2 02-CONTEXT)
//     the singular fields are sufficient.
//
// Phase 8 cleanup: collapse with SeriesDetailsLinks when Tv/ deletes.
import React, { useMemo } from 'react';
import Label from 'Components/Label';
import ClipboardButton from 'Components/Link/ClipboardButton';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import Manga from 'Manga/Manga';
import translate from 'Utilities/String/translate';
import styles from './MangaDetailsLinks.css';

type MangaDetailsLinksProps = Pick<
  Manga,
  'mangaDexId' | 'aniListId' | 'malId'
>;

interface MangaDetailsLink {
  externalId: string | number;
  name: string;
  url: string;
}

function MangaDetailsLinks(props: MangaDetailsLinksProps) {
  const { mangaDexId, aniListId, malId } = props;

  const links = useMemo(() => {
    const validLinks: MangaDetailsLink[] = [];

    if (mangaDexId) {
      validLinks.push({
        externalId: mangaDexId,
        name: 'MangaDex',
        url: `https://mangadex.org/title/${mangaDexId}`,
      });
    }

    if (aniListId) {
      validLinks.push({
        externalId: aniListId,
        name: 'AniList',
        url: `https://anilist.co/manga/${aniListId}`,
      });
    }

    if (malId) {
      validLinks.push({
        externalId: malId,
        name: 'MyAnimeList',
        url: `https://myanimelist.net/manga/${malId}`,
      });
    }

    return validLinks;
  }, [mangaDexId, aniListId, malId]);

  return (
    <div className={styles.links}>
      {links.map((link) => (
        <div key={link.name} className={styles.linkBlock}>
          <Link className={styles.link} to={link.url}>
            <Label
              className={styles.linkLabel}
              kind={kinds.INFO}
              size={sizes.LARGE}
            >
              {link.name}
            </Label>
          </Link>

          <ClipboardButton
            value={`${link.externalId}`}
            title={translate('CopyToClipboard')}
            kind={kinds.DEFAULT}
            size={sizes.SMALL}
            label={`${link.externalId}`}
          />
        </div>
      ))}
    </div>
  );
}

export default MangaDetailsLinks;
