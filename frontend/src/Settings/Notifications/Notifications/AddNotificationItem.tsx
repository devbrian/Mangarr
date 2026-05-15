import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import Menu from 'Components/Menu/Menu';
import MenuContent from 'Components/Menu/MenuContent';
import { sizes } from 'Helpers/Props';
import { SelectedSchema } from 'Settings/useProviderSchema';
import translate from 'Utilities/String/translate';
import { NotificationModel } from '../useConnections';
import AddNotificationPresetMenuItem from './AddNotificationPresetMenuItem';
import styles from './AddNotificationItem.css';

interface AddNotificationItemProps {
  implementation: string;
  implementationName: string;
  infoLink: string;
  presets?: NotificationModel[];
  onNotificationSelect: (selectedSchema: SelectedSchema) => void;
}

function AddNotificationItem({
  implementation,
  implementationName,
  infoLink,
  presets,
  onNotificationSelect,
}: AddNotificationItemProps) {
  const hasPresets = !!presets && !!presets.length;

  const handleNotificationSelect = useCallback(() => {
    onNotificationSelect({ implementation, implementationName });
  }, [implementation, implementationName, onNotificationSelect]);

  // Phase 20 Plan 20-06: testid slug matches the Mangarr implementation key
  // lowercased + suffix-stripped (same strip pattern as Plans 20-04/20-05 —
  // strips Indexer/DownloadClient/Notification suffix only). For
  // "KomgaNotification" → "komga"; "KavitaNotification" → "kavita". Stable for
  // Playwright targeting in SettingsProviderFlow.OpenPickerAndSelectAsync.
  const itemTestIdSlug = implementation
    .replace(/Indexer$/i, '')
    .replace(/DownloadClient$/i, '')
    .replace(/Notification$/i, '')
    .toLowerCase();

  return (
    <div
      className={styles.notification}
      data-testid={`add-notification-${itemTestIdSlug}`}
    >
      <Link className={styles.underlay} onPress={handleNotificationSelect} />

      <div className={styles.overlay}>
        <div className={styles.name}>{implementationName}</div>

        <div className={styles.actions}>
          {hasPresets ? (
            <span>
              <Button size={sizes.SMALL} onPress={handleNotificationSelect}>
                {translate('Custom')}
              </Button>

              <Menu className={styles.presetsMenu}>
                <Button className={styles.presetsMenuButton} size={sizes.SMALL}>
                  {translate('Presets')}
                </Button>

                <MenuContent>
                  {presets.map((preset) => {
                    return (
                      <AddNotificationPresetMenuItem
                        key={preset.name}
                        name={preset.name}
                        implementation={implementation}
                        implementationName={implementationName}
                        onPress={onNotificationSelect}
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

export default AddNotificationItem;
