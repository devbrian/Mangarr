import React, { useCallback, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { inputTypes, kinds } from 'Helpers/Props';
import {
  saveDelayProfile,
  setDelayProfileValue,
} from 'Store/Actions/settingsActions';
import selectSettings from 'Store/Selectors/selectSettings';
import DelayProfile from 'typings/DelayProfile';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './EditDelayProfileModalContent.css';

// Phase 23 D-04 + D-12 — PreferredProtocol stays on entity/Resource/typing
// (forward-compatible with future DownloadProtocol.Direct / .Scraper). The
// dropdown is hidden in the UI today because Mangarr only ships a single
// HTTP protocol; the field is set to 'http' server-side via the seed and
// round-trips through this modal unchanged.
const newDelayProfile: DelayProfile & { [key: string]: unknown } = {
  id: 0,
  name: '',
  order: 0,
  preferredProtocol: 'http',
  httpDelay: 0,
  bypassIfHighestQuality: false,
  bypassIfAboveCustomFormatScore: false,
  minimumCustomFormatScore: 0,
  tags: [],
};

function createDelayProfileSelector(id: number | undefined) {
  return createSelector(
    (state: AppState) => state.settings.delayProfiles,
    (delayProfiles) => {
      const { isFetching, error, isSaving, saveError, pendingChanges, items } =
        delayProfiles;

      const profile = id ? items.find((i) => i.id === id) : newDelayProfile;
      const settings = selectSettings<DelayProfile>(
        profile!,
        pendingChanges,
        saveError
      );

      return {
        isFetching,
        error,
        isSaving,
        saveError,
        item: settings.settings,
        ...settings,
      };
    }
  );
}

export interface EditDelayProfileModalContentProps {
  id?: number;
  onDeleteDelayProfilePress?: () => void;
  onModalClose: () => void;
}

function EditDelayProfileModalContent({
  id,
  onModalClose,
  onDeleteDelayProfilePress,
  ...otherProps
}: EditDelayProfileModalContentProps) {
  const dispatch = useDispatch();

  const { item, isFetching, error, isSaving, saveError } = useSelector(
    createDelayProfileSelector(id)
  );

  const {
    httpDelay,
    bypassIfHighestQuality,
    bypassIfAboveCustomFormatScore,
    minimumCustomFormatScore,
    tags,
  } = item;

  const wasSaving = usePrevious(isSaving);

  const onInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      // @ts-expect-error - actions are not typed
      dispatch(setDelayProfileValue({ name, value }));
    },
    [dispatch]
  );

  const handleSavePress = useCallback(() => {
    dispatch(saveDelayProfile({ id }));
  }, [id, dispatch]);

  useEffect(() => {
    if (!id) {
      Object.keys(newDelayProfile).forEach((name) => {
        if (name === 'id') {
          return;
        }

        dispatch(
          // @ts-expect-error - actions are not typed
          setDelayProfileValue({
            name,
            value: newDelayProfile[name],
          })
        );
      });
    }
  }, [id, dispatch]);

  useEffect(() => {
    if (wasSaving && !isSaving && !saveError) {
      onModalClose();
    }
  }, [isSaving, wasSaving, saveError, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {id ? translate('EditDelayProfile') : translate('AddDelayProfile')}
      </ModalHeader>

      <ModalBody>
        {isFetching ? <LoadingIndicator /> : null}

        {!isFetching && !!error ? (
          <Alert kind={kinds.DANGER}>{translate('AddDelayProfileError')}</Alert>
        ) : null}

        {!isFetching && !error ? (
          <Form {...otherProps}>
            <FormGroup>
              <FormLabel>{translate('HttpDelay')}</FormLabel>

              <FormInputGroup
                type={inputTypes.NUMBER}
                name="httpDelay"
                unit="minutes"
                {...httpDelay}
                helpText={translate('HttpDelayHelpText')}
                onChange={onInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>{translate('BypassDelayIfHighestQuality')}</FormLabel>

              <FormInputGroup
                type={inputTypes.CHECK}
                name="bypassIfHighestQuality"
                {...bypassIfHighestQuality}
                helpText={translate('BypassDelayIfHighestQualityHelpText')}
                onChange={onInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>
                {translate('BypassDelayIfAboveCustomFormatScore')}
              </FormLabel>

              <FormInputGroup
                type={inputTypes.CHECK}
                name="bypassIfAboveCustomFormatScore"
                {...bypassIfAboveCustomFormatScore}
                helpText={translate(
                  'BypassDelayIfAboveCustomFormatScoreHelpText'
                )}
                onChange={onInputChange}
              />
            </FormGroup>

            {bypassIfAboveCustomFormatScore.value ? (
              <FormGroup>
                <FormLabel>
                  {translate('BypassDelayIfAboveCustomFormatScoreMinimumScore')}
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="minimumCustomFormatScore"
                  {...minimumCustomFormatScore}
                  helpText={translate(
                    'BypassDelayIfAboveCustomFormatScoreMinimumScoreHelpText'
                  )}
                  onChange={onInputChange}
                />
              </FormGroup>
            ) : null}

            {id === 1 ? (
              <Alert>{translate('DefaultDelayProfileManga')}</Alert>
            ) : (
              <FormGroup>
                <FormLabel>{translate('Tags')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.TAG}
                  name="tags"
                  {...tags}
                  helpText={translate('DelayProfileMangaTagsHelpText')}
                  onChange={onInputChange}
                />
              </FormGroup>
            )}
          </Form>
        ) : null}
      </ModalBody>

      <ModalFooter>
        {id && id > 1 ? (
          <Button
            className={styles.deleteButton}
            kind={kinds.DANGER}
            onPress={onDeleteDelayProfilePress}
          >
            {translate('Delete')}
          </Button>
        ) : null}

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          isSpinning={isSaving}
          error={saveError}
          onPress={handleSavePress}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditDelayProfileModalContent;
