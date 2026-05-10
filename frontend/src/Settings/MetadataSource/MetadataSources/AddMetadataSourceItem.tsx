import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import Menu from 'Components/Menu/Menu';
import MenuContent from 'Components/Menu/MenuContent';
import { sizes } from 'Helpers/Props';
import { SelectedSchema } from 'Settings/useProviderSchema';
import translate from 'Utilities/String/translate';
import { MetadataSourceModel } from '../useMetadataSources';
import AddMetadataSourcePresetMenuItem from './AddMetadataSourcePresetMenuItem';
import styles from './AddMetadataSourceItem.css';

interface AddMetadataSourceItemProps {
  implementation: string;
  implementationName: string;
  infoLink: string;
  presets?: MetadataSourceModel[];
  onMetadataSourceSelect: (selectedSchema: SelectedSchema) => void;
}

function AddMetadataSourceItem({
  implementation,
  implementationName,
  infoLink,
  presets,
  onMetadataSourceSelect,
}: AddMetadataSourceItemProps) {
  const hasPresets = !!presets && !!presets.length;

  const handleMetadataSourceSelect = useCallback(() => {
    onMetadataSourceSelect({ implementation, implementationName });
  }, [implementation, implementationName, onMetadataSourceSelect]);

  return (
    <div className={styles.metadataSource}>
      <Link className={styles.underlay} onPress={handleMetadataSourceSelect} />

      <div className={styles.overlay}>
        <div className={styles.name}>{implementationName}</div>

        <div className={styles.actions}>
          {hasPresets ? (
            <span>
              <Button size={sizes.SMALL} onPress={handleMetadataSourceSelect}>
                {translate('Custom')}
              </Button>

              <Menu className={styles.presetsMenu}>
                <Button className={styles.presetsMenuButton} size={sizes.SMALL}>
                  {translate('Presets')}
                </Button>

                <MenuContent>
                  {presets.map((preset) => {
                    return (
                      <AddMetadataSourcePresetMenuItem
                        key={preset.name}
                        name={preset.name}
                        implementation={implementation}
                        implementationName={implementationName}
                        onPress={onMetadataSourceSelect}
                      />
                    );
                  })}
                </MenuContent>
              </Menu>
            </span>
          ) : null}

          {infoLink ? (
            <Button to={infoLink} size={sizes.SMALL}>
              {translate('MoreInfo')}
            </Button>
          ) : null}
        </div>
      </div>
    </div>
  );
}

export default AddMetadataSourceItem;
