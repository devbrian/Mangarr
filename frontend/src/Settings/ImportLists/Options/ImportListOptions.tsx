import React, { useEffect } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import { kinds } from 'Helpers/Props';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import {
  OnChildStateChange,
  SetChildSave,
} from 'typings/Settings/SettingsState';
import translate from 'Utilities/String/translate';

// Phase 26 Plan 26-05 (IL-05) — ImportList Options section. Sibling to
// `frontend/src/Settings/Indexers/Options/IndexerOptions.tsx` per RESEARCH §Q7,
// but the Sonarr-canonical IndexerOptions backs `/api/v5/settings/indexer` and
// no equivalent `/api/v5/settings/importlist` controller exists in the Phase 26
// substrate (Plan 26-04 ships zero settings-backing controllers per D-08).
//
// Phase 27 will land a `ImportListSettingsController` if/when provider-level
// options (CleanLibraryLevel, ListSyncTag, etc.) need a backing store. Until
// then this component renders an advanced-only info Alert + wires the
// child-save / child-state callbacks as no-ops so the parent
// ImportListSettings.tsx (mirroring IndexerSettings.tsx) does not throw at
// useEffect-wiring time.

interface ImportListOptionsProps {
  setChildSave: SetChildSave;
  onChildStateChange: OnChildStateChange;
}

function ImportListOptions({
  setChildSave,
  onChildStateChange,
}: ImportListOptionsProps) {
  const showAdvancedSettings = useShowAdvancedSettings();

  useEffect(() => {
    // No backing settings controller in Phase 26 substrate — no-op save.
    setChildSave(() => undefined);
  }, [setChildSave]);

  useEffect(() => {
    // Always clean: no pending changes possible without form inputs.
    onChildStateChange({
      isSaving: false,
      hasPendingChanges: false,
    });
  }, [onChildStateChange]);

  if (!showAdvancedSettings) {
    return null;
  }

  return (
    <FieldSet legend={translate('Options')}>
      <Alert kind={kinds.INFO}>
        {translate('ImportListOptionsPhase26Notice')}
      </Alert>
    </FieldSet>
  );
}

export default ImportListOptions;
