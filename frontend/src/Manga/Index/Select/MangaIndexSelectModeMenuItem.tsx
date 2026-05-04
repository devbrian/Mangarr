import React, { useCallback } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import { IconName } from 'Components/Icon';
import PageToolbarOverflowMenuItem from 'Components/Page/Toolbar/PageToolbarOverflowMenuItem';

interface MangaIndexSelectModeMenuItemProps {
  label: string;
  iconName: IconName;
  isSelectMode: boolean;
  onPress: () => void;
}

function MangaIndexSelectModeMenuItem(
  props: MangaIndexSelectModeMenuItemProps
) {
  const { label, iconName, isSelectMode, onPress } = props;
  const { reset } = useSelect();

  const onPressWrapper = useCallback(() => {
    if (isSelectMode) {
      reset();
    }

    onPress();
  }, [isSelectMode, onPress, reset]);

  return (
    <PageToolbarOverflowMenuItem
      label={label}
      iconName={iconName}
      onPress={onPressWrapper}
    />
  );
}

export default MangaIndexSelectModeMenuItem;
