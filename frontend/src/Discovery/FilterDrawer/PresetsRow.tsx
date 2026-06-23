// Quick task 260623-kar — Discovery named saved-filter presets row.
// Mirrors the existing FilterDrawer sub-component pattern (TagTypeahead / TristateChip)
// to keep FilterDrawer.tsx from bloating. NEW-in-Mangarr; the bespoke Discovery-drawer
// divergence (DIVERGENCE.md Phase 42) — reuses EnhancedSelectInput / TextInput / Button,
// NOT Sonarr's Components/Filter saved-filter FilterBuilder machinery.
//
// Save snapshots the LIVE working filters (discoveryOptionsStore) EXCLUDING `topX`
// (destructured off — it is a toolbar add-count, not a filter) into the additive
// discoveryPresetsStore. Apply writes a stored snapshot back through
// setDiscoveryOptions (shallow-merge preserves the live `topX`); the drawer chips/ranges
// re-render because FilterDrawer subscribes via useDiscoveryOptions().
import React, { useCallback, useState } from 'react';
import EnhancedSelectInput from 'Components/Form/Select/EnhancedSelectInput';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import {
  getDiscoveryOptions,
  setDiscoveryOptions,
} from 'Discovery/discoveryOptionsStore';
import { useDiscoveryPresetsStore } from 'Discovery/discoveryPresetsStore';
import { kinds } from 'Helpers/Props';
import { EnhancedSelectInputChanged, InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './PresetsRow.css';

function PresetsRow() {
  const presets = useDiscoveryPresetsStore((state) => state.presets);
  const addPreset = useDiscoveryPresetsStore((state) => state.addPreset);
  const deletePreset = useDiscoveryPresetsStore((state) => state.deletePreset);

  const [name, setName] = useState('');
  const [selectedPresetId, setSelectedPresetId] = useState('');

  const handleNameChange = useCallback(({ value }: InputChanged<string>) => {
    setName(value);
  }, []);

  const handleSave = useCallback(() => {
    const trimmed = name.trim();

    if (!trimmed) {
      return;
    }

    // Snapshot the live filters, dropping the toolbar add-count (topX) so presets
    // stay purely about the query (CONTEXT.md LOCKED decision).
    const { topX, ...filters } = getDiscoveryOptions();
    addPreset(trimmed, filters);
    setName('');
  }, [name, addPreset]);

  const handleSelectChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string>) => {
      setSelectedPresetId(value);
    },
    []
  );

  const handleApply = useCallback(() => {
    const preset = presets.find((p) => p.id === selectedPresetId);

    if (preset) {
      setDiscoveryOptions(preset.filters);
    }
  }, [presets, selectedPresetId]);

  const handleDelete = useCallback(() => {
    if (selectedPresetId) {
      deletePreset(selectedPresetId);
      setSelectedPresetId('');
    }
  }, [selectedPresetId, deletePreset]);

  const selectValues = [
    { key: '', value: translate('DiscoveryPresetSelectPlaceholder') },
    ...presets.map((preset) => ({ key: preset.id, value: preset.name })),
  ];

  const hasSelection = selectedPresetId !== '';

  return (
    <div className={styles.presetsRow} data-testid="discovery-presets-row">
      <div className={styles.sectionLabel}>{translate('DiscoveryPresets')}</div>

      {presets.length > 0 ? (
        <div className={styles.applyRow}>
          {/* EnhancedSelectInput does not forward data-testid (no otherProps
              spread), so the harness selector lives on this wrapper. */}
          <div className={styles.select} data-testid="discovery-preset-select">
            <EnhancedSelectInput
              name="discoveryPreset"
              value={selectedPresetId}
              values={selectValues}
              onChange={handleSelectChange}
            />
          </div>

          <Button
            kind={kinds.PRIMARY}
            isDisabled={!hasSelection}
            data-testid="discovery-preset-apply"
            onPress={handleApply}
          >
            {translate('Apply')}
          </Button>

          <Button
            kind={kinds.DANGER}
            isDisabled={!hasSelection}
            data-testid="discovery-preset-delete"
            onPress={handleDelete}
          >
            {translate('Delete')}
          </Button>
        </div>
      ) : (
        <div className={styles.empty} data-testid="discovery-preset-empty">
          {translate('DiscoveryPresetEmpty')}
        </div>
      )}

      <div className={styles.saveRow}>
        <TextInput
          name="discoveryPresetName"
          value={name}
          placeholder={translate('DiscoveryPresetNamePlaceholder')}
          data-testid="discovery-preset-name-input"
          onChange={handleNameChange}
        />

        <Button
          isDisabled={name.trim() === ''}
          data-testid="discovery-preset-save"
          onPress={handleSave}
        >
          {translate('Save')}
        </Button>
      </div>
    </div>
  );
}

export default PresetsRow;
