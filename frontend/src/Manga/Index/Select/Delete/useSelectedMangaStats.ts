import { useMemo } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import Manga from 'Manga/Manga';
import useManga from 'Manga/useManga';
import sortByProp from 'Utilities/Array/sortByProp';

function useSelectedMangaStats() {
  const { data: allSeries } = useManga();
  const { useSelectedIds } = useSelect<Manga>();
  const mangaIds = useSelectedIds();

  const manga = useMemo((): Manga[] => {
    const seriesList = mangaIds.map((id) => {
      return allSeries.find((s) => s.id === id);
    }) as Manga[];

    return seriesList.sort(sortByProp('sortTitle'));
  }, [allSeries, mangaIds]);

  const { totalEpisodeFileCount, totalSizeOnDisk } = useMemo(() => {
    return manga.reduce(
      (acc, { statistics = {} }) => {
        // Sonarr divergence: Phase 17.3 D-13/D-14 — destructure
        // chapterFileCount (manga-canonical) instead of episodeFileCount
        // (Manga.ts D-13 trim removed the TV-shape episodeFileCount field
        // from Statistics). The accumulator key `totalEpisodeFileCount`
        // is left as-is to preserve the public return shape consumed by
        // DeleteMangaModalContent + DeleteMangaFilesModalContent (full
        // rename deferred outside this plan's 11-file scope).
        const { chapterFileCount = 0, sizeOnDisk = 0 } = statistics;

        acc.totalEpisodeFileCount += chapterFileCount;
        acc.totalSizeOnDisk += sizeOnDisk;

        return acc;
      },
      {
        totalEpisodeFileCount: 0,
        totalSizeOnDisk: 0,
      }
    );
  }, [manga]);

  return {
    manga,
    mangaIds,
    totalEpisodeFileCount,
    totalSizeOnDisk,
  };
}

export default useSelectedMangaStats;
