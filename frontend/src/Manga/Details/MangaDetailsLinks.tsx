// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/SeriesDetailsLinks.tsx
// (TVDB / TVMaze / IMDB / TMDB → MangaDex / AniList / MyAnimeList).
//
// Manga sibling preserves: link-block render + ClipboardButton + Label kinds.
// Manga sibling diverges from SeriesDetailsLinks:
//   * External-source set replaced — TVDB/TVMaze/IMDB/TMDB drop, MangaDex/
//     AniList/MAL surface (+ the MangaBaka cross-source ids added by
//     quick-260608-l2e: Kitsu / MangaUpdates / AnimePlanet / AnimeNewsNetwork /
//     Shikimori). URLs sourced from each provider's canonical manga-detail path:
//       MangaDex         → https://mangadex.org/title/{uuid}
//       AniList          → https://anilist.co/manga/{id}
//       MyAnimeList      → https://myanimelist.net/manga/{id}
//       Kitsu            → https://kitsu.app/manga/{id}
//       MangaUpdates     → https://www.mangaupdates.com/series/{token}
//       AnimePlanet      → https://www.anime-planet.com/manga/{slug}
//       AnimeNewsNetwork → https://www.animenewsnetwork.com/encyclopedia/manga.php?id={id}
//       Shikimori        → https://shikimori.one/mangas/{id}
//     The two free-form STRING ids (animePlanetId, mangaUpdatesId) are wrapped in
//     encodeURIComponent so an upstream-controlled slug cannot break the URL
//     path/query (T-l2e-01); numeric ids need no encoding.
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
  | 'mangaDexId'
  | 'aniListId'
  | 'malId'
  | 'kitsuId'
  | 'animeNewsNetworkId'
  | 'shikimoriId'
  | 'animePlanetId'
  | 'mangaUpdatesId'
>;

interface MangaDetailsLink {
  externalId: string | number;
  name: string;
  url: string;
}

function MangaDetailsLinks(props: MangaDetailsLinksProps) {
  const {
    mangaDexId,
    aniListId,
    malId,
    kitsuId,
    animeNewsNetworkId,
    shikimoriId,
    animePlanetId,
    mangaUpdatesId,
  } = props;

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

    if (kitsuId) {
      validLinks.push({
        externalId: kitsuId,
        name: 'Kitsu',
        url: `https://kitsu.app/manga/${kitsuId}`,
      });
    }

    if (mangaUpdatesId) {
      validLinks.push({
        externalId: mangaUpdatesId,
        name: 'MangaUpdates',
        url: `https://www.mangaupdates.com/series/${encodeURIComponent(
          mangaUpdatesId
        )}`,
      });
    }

    if (animePlanetId) {
      validLinks.push({
        externalId: animePlanetId,
        name: 'AnimePlanet',
        url: `https://www.anime-planet.com/manga/${encodeURIComponent(
          animePlanetId
        )}`,
      });
    }

    if (animeNewsNetworkId) {
      validLinks.push({
        externalId: animeNewsNetworkId,
        name: 'AnimeNewsNetwork',
        url: `https://www.animenewsnetwork.com/encyclopedia/manga.php?id=${animeNewsNetworkId}`,
      });
    }

    if (shikimoriId) {
      validLinks.push({
        externalId: shikimoriId,
        name: 'Shikimori',
        url: `https://shikimori.one/mangas/${shikimoriId}`,
      });
    }

    return validLinks;
  }, [
    mangaDexId,
    aniListId,
    malId,
    kitsuId,
    animeNewsNetworkId,
    shikimoriId,
    animePlanetId,
    mangaUpdatesId,
  ]);

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
