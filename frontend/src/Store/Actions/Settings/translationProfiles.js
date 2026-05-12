// Phase 17 follow-up (debug qualityprofiles-redux-rename, 2026-05-12 — GH #82
// Path 1 surface-rename cascade): renamed from `qualityProfiles.js` and
// repointed to `/api/v5/translationprofile` (the `/api/v5/qualityprofile`
// endpoint was deleted in Phase 15 Plan 15-03 D-12). The action-type
// namespace (`settings/translationProfiles/*`) and section key
// (`settings.translationProfiles`) match the manga-canonical
// TranslationProfile axis. This slice is currently not registered in
// `settingsActions.js` (orphan from before — kept around for any future
// Redux-driven TranslationProfile flow that wants the legacy createThunk +
// reducer scaffold instead of React Query). The live TranslationProfile
// editor (`Settings/Profiles/Translations/`) uses React Query via
// `useApiQuery`/`useApiMutation` directly and does not rely on this slice.
import { createAction } from 'redux-actions';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createFetchSchemaHandler from 'Store/Actions/Creators/createFetchSchemaHandler';
import createRemoveItemHandler from 'Store/Actions/Creators/createRemoveItemHandler';
import createSaveProviderHandler from 'Store/Actions/Creators/createSaveProviderHandler';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';
import translate from 'Utilities/String/translate';

//
// Variables

const section = 'settings.translationProfiles';

//
// Actions Types

export const FETCH_TRANSLATION_PROFILES = 'settings/translationProfiles/fetchTranslationProfiles';
export const FETCH_TRANSLATION_PROFILE_SCHEMA = 'settings/translationProfiles/fetchTranslationProfileSchema';
export const SAVE_TRANSLATION_PROFILE = 'settings/translationProfiles/saveTranslationProfile';
export const DELETE_TRANSLATION_PROFILE = 'settings/translationProfiles/deleteTranslationProfile';
export const SET_TRANSLATION_PROFILE_VALUE = 'settings/translationProfiles/setTranslationProfileValue';
export const CLONE_TRANSLATION_PROFILE = 'settings/translationProfiles/cloneTranslationProfile';

//
// Action Creators

export const fetchTranslationProfiles = createThunk(FETCH_TRANSLATION_PROFILES);
export const fetchTranslationProfileSchema = createThunk(FETCH_TRANSLATION_PROFILE_SCHEMA);
export const saveTranslationProfile = createThunk(SAVE_TRANSLATION_PROFILE);
export const deleteTranslationProfile = createThunk(DELETE_TRANSLATION_PROFILE);

export const setTranslationProfileValue = createAction(SET_TRANSLATION_PROFILE_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

export const cloneTranslationProfile = createAction(CLONE_TRANSLATION_PROFILE);

//
// Details

export default {

  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    isDeleting: false,
    deleteError: null,
    isSchemaFetching: false,
    isSchemaPopulated: false,
    schemaError: null,
    schema: {},
    isSaving: false,
    saveError: null,
    items: [],
    pendingChanges: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_TRANSLATION_PROFILES]: createFetchHandler(section, '/translationprofile'),
    [FETCH_TRANSLATION_PROFILE_SCHEMA]: createFetchSchemaHandler(section, '/translationprofile/schema'),
    [SAVE_TRANSLATION_PROFILE]: createSaveProviderHandler(section, '/translationprofile'),
    [DELETE_TRANSLATION_PROFILE]: createRemoveItemHandler(section, '/translationprofile')
  },

  //
  // Reducers

  reducers: {
    [SET_TRANSLATION_PROFILE_VALUE]: createSetSettingValueReducer(section),

    [CLONE_TRANSLATION_PROFILE]: function(state, { payload }) {
      const id = payload.id;
      const newState = getSectionState(state, section);
      const item = newState.items.find((i) => i.id === id);
      const pendingChanges = { ...item, id: 0 };
      delete pendingChanges.id;

      pendingChanges.name = translate('DefaultNameCopiedProfile', { name: pendingChanges.name });
      newState.pendingChanges = pendingChanges;

      return updateSectionState(state, section, newState);
    }
  }

};
