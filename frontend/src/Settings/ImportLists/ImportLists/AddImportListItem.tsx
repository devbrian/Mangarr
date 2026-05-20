import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import Menu from 'Components/Menu/Menu';
import MenuContent from 'Components/Menu/MenuContent';
import { sizes } from 'Helpers/Props';
import { SelectedSchema } from 'Settings/useProviderSchema';
import translate from 'Utilities/String/translate';
import { ImportListModel } from '../useImportLists';
import AddImportListPresetMenuItem from './AddImportListPresetMenuItem';

// Phase 26 Plan 26-05 (IL-05) — single Add-picker tile. Mirror of
// frontend/src/Settings/Indexers/Indexers/AddIndexerItem.tsx per RESEARCH §Q7.
// testid slug strips the `ImportList` suffix per Plan 20-04 pattern (e.g.
// `MangaDexImportList` → `add-importlist-mangadex`); Phase 18 D-18 allowed
// prefix `add-importlist-*`.

interface AddImportListItemProps {
  implementation: string;
  implementationName: string;
  infoLink: string;
  presets?: ImportListModel[];
  onImportListSelect: (selectedSchema: SelectedSchema) => void;
}

function AddImportListItem({
  implementation,
  implementationName,
  infoLink,
  presets,
  onImportListSelect,
}: AddImportListItemProps) {
  const hasPresets = !!presets && !!presets.length;

  const handleImportListSelect = useCallback(() => {
    onImportListSelect({ implementation, implementationName });
  }, [implementation, implementationName, onImportListSelect]);

  const itemTestIdSlug = implementation
    .replace(/ImportList$/i, '')
    .toLowerCase();

  return (
    <div data-testid={`add-importlist-${itemTestIdSlug}`}>
      <Link onPress={handleImportListSelect} />

      <div>
        <div>{implementationName}</div>

        <div>
          {hasPresets && (
            <span>
              <Button size={sizes.SMALL} onPress={handleImportListSelect}>
                {translate('Custom')}
              </Button>

              <Menu>
                <Button size={sizes.SMALL}>{translate('Presets')}</Button>

                <MenuContent>
                  {presets.map((preset) => {
                    return (
                      <AddImportListPresetMenuItem
                        key={preset.name}
                        name={preset.name}
                        implementation={implementation}
                        implementationName={implementationName}
                        onPress={onImportListSelect}
                      />
                    );
                  })}
                </MenuContent>
              </Menu>
            </span>
          )}

          <Button to={infoLink} size={sizes.SMALL}>
            {translate('MoreInfo')}
          </Button>
        </div>
      </div>
    </div>
  );
}

export default AddImportListItem;
