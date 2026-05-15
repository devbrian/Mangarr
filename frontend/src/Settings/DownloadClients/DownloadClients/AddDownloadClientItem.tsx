import React, { useCallback } from 'react';
import { useDispatch } from 'react-redux';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import Menu from 'Components/Menu/Menu';
import MenuContent from 'Components/Menu/MenuContent';
import { sizes } from 'Helpers/Props';
import { selectDownloadClientSchema } from 'Store/Actions/settingsActions';
import DownloadClient from 'typings/DownloadClient';
import translate from 'Utilities/String/translate';
import AddDownloadClientPresetMenuItem from './AddDownloadClientPresetMenuItem';
import styles from './AddDownloadClientItem.css';

interface AddDownloadClientItemProps {
  implementation: string;
  implementationName: string;
  infoLink: string;
  presets?: DownloadClient[];
  onDownloadClientSelect: () => void;
}

function AddDownloadClientItem({
  implementation,
  implementationName,
  infoLink,
  presets,
  onDownloadClientSelect,
}: AddDownloadClientItemProps) {
  const dispatch = useDispatch();
  const hasPresets = !!presets && !!presets.length;

  const handleDownloadClientSelect = useCallback(() => {
    dispatch(
      selectDownloadClientSchema({
        implementation,
        implementationName,
      })
    );

    onDownloadClientSelect();
  }, [implementation, implementationName, dispatch, onDownloadClientSelect]);

  // Phase 20 Plan 20-05: testid slug matches the Mangarr implementation key
  // lowercased + suffix-stripped (same strip pattern as Plan 20-04's
  // AddIndexerItem.tsx — strips Indexer/DownloadClient/Notification suffix only).
  // For "InProcessImageDownloadClient" this yields "inprocessimage". Stable for
  // Playwright targeting in SettingsProviderFlow.OpenPickerAndSelectAsync.
  const itemTestIdSlug = implementation
    .replace(/Indexer$/i, '')
    .replace(/DownloadClient$/i, '')
    .replace(/Notification$/i, '')
    .toLowerCase();

  return (
    <div
      className={styles.downloadClient}
      data-testid={`add-downloadclient-${itemTestIdSlug}`}
    >
      <Link className={styles.underlay} onPress={handleDownloadClientSelect} />

      <div className={styles.overlay}>
        <div className={styles.name}>{implementationName}</div>

        <div className={styles.actions}>
          {hasPresets ? (
            <span>
              <Button size={sizes.SMALL} onPress={handleDownloadClientSelect}>
                {translate('Custom')}
              </Button>

              <Menu className={styles.presetsMenu}>
                <Button className={styles.presetsMenuButton} size={sizes.SMALL}>
                  {translate('Presets')}
                </Button>

                <MenuContent>
                  {presets.map((preset) => {
                    return (
                      <AddDownloadClientPresetMenuItem
                        key={preset.name}
                        name={preset.name}
                        implementation={implementation}
                        implementationName={implementationName}
                        onPress={onDownloadClientSelect}
                      />
                    );
                  })}
                </MenuContent>
              </Menu>
            </span>
          ) : null}

          <Button to={infoLink} size={sizes.SMALL}>
            {translate('MoreInfo')}
          </Button>
        </div>
      </div>
    </div>
  );
}

export default AddDownloadClientItem;
