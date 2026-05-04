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
        const { episodeFileCount = 0, sizeOnDisk = 0 } = statistics;

        acc.totalEpisodeFileCount += episodeFileCount;
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
