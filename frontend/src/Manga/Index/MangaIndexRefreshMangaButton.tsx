import React, { useCallback } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import { icons } from 'Helpers/Props';
import Manga from 'Manga/Manga';
import { useMangaIndex } from 'Manga/useManga';
import translate from 'Utilities/String/translate';

interface MangaIndexRefreshMangaButtonProps {
  isSelectMode: boolean;
  selectedFilterKey: string | number;
}

function MangaIndexRefreshMangaButton(
  props: MangaIndexRefreshMangaButtonProps
) {
  const isRefreshing = useCommandExecuting(CommandNames.RefreshSeries);
  const { data, totalItems } = useMangaIndex();

  const executeCommand = useExecuteCommand();
  const { isSelectMode, selectedFilterKey } = props;
  const { anySelected, getSelectedIds } = useSelect<Manga>();

  let refreshLabel = translate('UpdateAll');

  if (anySelected) {
    refreshLabel = translate('UpdateSelected');
  } else if (selectedFilterKey !== 'all') {
    refreshLabel = translate('UpdateFiltered');
  }

  const onPress = useCallback(() => {
    const mangaToRefresh =
      isSelectMode && anySelected ? getSelectedIds() : data.map((m) => m.id);

    executeCommand({
      name: CommandNames.RefreshSeries,
      seriesIds: mangaToRefresh,
    });
  }, [executeCommand, anySelected, isSelectMode, data, getSelectedIds]);

  return (
    <PageToolbarButton
      label={refreshLabel}
      isSpinning={isRefreshing}
      isDisabled={!totalItems}
      iconName={icons.REFRESH}
      onPress={onPress}
    />
  );
}

export default MangaIndexRefreshMangaButton;
