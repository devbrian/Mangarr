import React, { useCallback, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { inputTypes, kinds } from 'Helpers/Props';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import {
  fetchImportListOptions,
  saveImportListOptions,
  setImportListOptionsValue,
} from 'Store/Actions/settingsActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import {
  OnChildStateChange,
  SetChildSave,
} from 'typings/Settings/SettingsState';
import translate from 'Utilities/String/translate';

// Phase 27.1 Plan 27.1-02 — Replaces the Phase 26 Plan 26-05 Alert placeholder
// (which referenced the now-removed `ImportListOptionsPhase26Notice` key) with
// the Sonarr v5-develop verbatim Redux-thunk Options form. Backend controller
// `ImportListConfigController` shipped in Plan 27.1-01 satisfies the
// `GET/PUT /api/v5/config/importlist` round-trip the Phase 26 Redux store at
// `Store/Actions/Settings/importListOptions.js:53-54` was already wired to call.
//
// Analog: frontend/src/Settings/DownloadClients/Options/DownloadClientOptions.tsx
// (Redux thunk + createSettingsSectionSelector; NOT the React Query path used
// by IndexerOptions.tsx). See plan 27.1-PATTERNS.md "Pattern κ" + RESEARCH OQ 4.

const SECTION = 'importListOptions';

// Late-bind translate() per RESEARCH Pitfall 6: getters delay translation
// resolution until first read, avoiding translate-at-module-load HMR crashes.
// Enum keys MUST stay camelCase Sonarr-canonical (`disabled` / `logOnly` /
// `keepAndUnmonitor` / `keepAndTag`) — backend serializes ListSyncLevelType
// to those camelCase strings. Only the `value: translate(...)` strings are
// manga-shaped via the new `KeepAndUnmonitorManga` / `KeepAndTagManga` keys
// (RESEARCH OQ 3 — Mangarr en.json convention is entity-suffixed).
const cleanLibraryLevelOptions: EnhancedSelectInputValue<string>[] = [
  {
    key: 'disabled',
    get value() {
      return translate('Disabled');
    },
  },
  {
    key: 'logOnly',
    get value() {
      return translate('LogOnly');
    },
  },
  {
    key: 'keepAndUnmonitor',
    get value() {
      return translate('KeepAndUnmonitorManga');
    },
  },
  {
    key: 'keepAndTag',
    get value() {
      return translate('KeepAndTagManga');
    },
  },
];

interface ImportListOptionsProps {
  setChildSave: SetChildSave;
  onChildStateChange: OnChildStateChange;
}

function ImportListOptions({
  setChildSave,
  onChildStateChange,
}: ImportListOptionsProps) {
  const dispatch = useDispatch();
  const showAdvancedSettings = useShowAdvancedSettings();

  const {
    isSaving,
    hasPendingChanges,
    isFetching,
    error,
    settings,
    hasSettings,
  } = useSelector(createSettingsSectionSelector(SECTION));

  const { listSyncLevel, listSyncTag } = settings;

  const onInputChange = useCallback(
    ({ name, value }: { name: string; value: unknown }) => {
      // @ts-expect-error 'setImportListOptionsValue' isn't typed yet
      dispatch(setImportListOptionsValue({ name, value }));
    },
    [dispatch]
  );

  // `inputTypes.SERIES_TAG` is the Sonarr-canonical legacy name for what
  // is impl'd in Mangarr as `MangaTagInput` (see Helpers/Props/inputTypes.ts
  // line 25 + Components/Form/FormInputGroup.tsx line 92 — seriesTag maps to
  // MangaTagInput). The picker emits `number[]`; we collapse to a single id
  // because `listSyncTag` is a scalar in `ImportListOptionsSettings`.
  const onTagChange = useCallback(
    ({ name, value }: { name: string; value: number[] }) => {
      const id = value.length === 0 ? 0 : value[value.length - 1];
      // @ts-expect-error 'setImportListOptionsValue' isn't typed yet
      dispatch(setImportListOptionsValue({ name, value: id }));
    },
    [dispatch]
  );

  useEffect(() => {
    dispatch(fetchImportListOptions());
    setChildSave(() => dispatch(saveImportListOptions()));

    return () => {
      dispatch(clearPendingChanges({ section: `settings.${SECTION}` }));
    };
  }, [dispatch, setChildSave]);

  useEffect(() => {
    onChildStateChange({
      isSaving,
      hasPendingChanges,
    });
  }, [onChildStateChange, isSaving, hasPendingChanges]);

  if (!showAdvancedSettings) {
    return null;
  }

  return (
    <div data-testid="settings-importlists-options">
      <FieldSet legend={translate('Options')}>
        {isFetching ? <LoadingIndicator /> : null}

        {!isFetching && error ? (
          <Alert kind={kinds.DANGER}>{translate('ListOptionsLoadError')}</Alert>
        ) : null}

        {hasSettings && !isFetching && !error ? (
          <Form>
            <FormGroup
              advancedSettings={showAdvancedSettings}
              isAdvanced={true}
            >
              <FormLabel>{translate('CleanLibraryLevel')}</FormLabel>
              <FormInputGroup
                type={inputTypes.SELECT}
                name="listSyncLevel"
                values={cleanLibraryLevelOptions}
                helpText={translate('ListSyncLevelHelpText')}
                onChange={onInputChange}
                {...listSyncLevel}
              />
            </FormGroup>

            {listSyncLevel.value === 'keepAndTag' ? (
              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
              >
                <FormLabel>{translate('ListSyncTag')}</FormLabel>
                <FormInputGroup
                  {...listSyncTag}
                  type={inputTypes.SERIES_TAG}
                  name="listSyncTag"
                  value={listSyncTag.value === 0 ? [] : [listSyncTag.value]}
                  helpText={translate('ListSyncTagHelpText')}
                  onChange={onTagChange}
                />
              </FormGroup>
            ) : null}
          </Form>
        ) : null}
      </FieldSet>
    </div>
  );
}

export default ImportListOptions;
