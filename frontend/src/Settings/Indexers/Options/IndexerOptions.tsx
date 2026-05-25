import React, { useCallback, useEffect } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { inputTypes, kinds } from 'Helpers/Props';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import { InputChanged } from 'typings/inputs';
import {
  OnChildStateChange,
  SetChildSave,
} from 'typings/Settings/SettingsState';
import translate from 'Utilities/String/translate';
import {
  useManageIndexerSettings,
  useTestCloudflareSolver,
} from './useIndexerSettings';

interface IndexerOptionsProps {
  setChildSave: SetChildSave;
  onChildStateChange: OnChildStateChange;
}

function IndexerOptions({
  setChildSave,
  onChildStateChange,
}: IndexerOptionsProps) {
  const {
    isFetching,
    isFetched,
    isSaving,
    error,
    settings,
    hasSettings,
    hasPendingChanges,
    saveSettings,
    updateSetting,
  } = useManageIndexerSettings();

  const showAdvancedSettings = useShowAdvancedSettings();

  const {
    mutate: testSolver,
    isPending: isTesting,
    error: testError,
    data: testResult,
    reset: resetTestResult,
  } = useTestCloudflareSolver();

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      // WR-06: the Test result reflects the solver URL at probe time. Clear any stale
      // success/error verdict when the URL changes so the operator can't mistake a prior
      // probe's verdict for the just-edited (untested) URL.
      if (name === 'cloudflareSolverUrl') {
        resetTestResult();
      }

      // @ts-expect-error - InputChanged name/value are not typed as keyof IndexerSettingsModel
      updateSetting(name, value);
    },
    [updateSetting, resetTestResult]
  );

  const handleTestPress = useCallback(() => {
    testSolver();
  }, [testSolver]);

  useEffect(() => {
    setChildSave(saveSettings);
  }, [saveSettings, setChildSave]);

  useEffect(() => {
    onChildStateChange({
      isSaving,
      hasPendingChanges,
    });
  }, [hasPendingChanges, isSaving, onChildStateChange]);

  return (
    <FieldSet legend={translate('Options')}>
      {isFetching ? <LoadingIndicator /> : null}

      {!isFetching && error ? (
        <Alert kind={kinds.DANGER}>
          {translate('IndexerOptionsLoadError')}
        </Alert>
      ) : null}

      {hasSettings && isFetched && !error ? (
        <Form>
          <FormGroup>
            <FormLabel>{translate('MinimumAge')}</FormLabel>

            <FormInputGroup
              type={inputTypes.NUMBER}
              name="minimumAge"
              min={0}
              unit="minutes"
              helpText={translate('MinimumAgeHelpText')}
              onChange={handleInputChange}
              {...settings.minimumAge}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('Retention')}</FormLabel>

            <FormInputGroup
              type={inputTypes.NUMBER}
              name="retention"
              min={0}
              unit="days"
              helpText={translate('RetentionHelpText')}
              onChange={handleInputChange}
              {...settings.retention}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('MaximumSize')}</FormLabel>

            <FormInputGroup
              type={inputTypes.NUMBER}
              name="maximumSize"
              min={0}
              unit="MB"
              helpText={translate('MaximumSizeHelpText')}
              onChange={handleInputChange}
              {...settings.maximumSize}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('CloudflareSolverUrl')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TEXT}
              name="cloudflareSolverUrl"
              helpText={translate('CloudflareSolverUrlHelpText')}
              onChange={handleInputChange}
              {...settings.cloudflareSolverUrl}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('Test')}</FormLabel>

            <div>
              <SpinnerErrorButton
                isSpinning={isTesting}
                error={testError}
                onPress={handleTestPress}
              >
                {translate('CloudflareSolverTestConnection')}
              </SpinnerErrorButton>

              {!isTesting && testResult ? (
                <Alert
                  kind={testResult.isValid ? kinds.SUCCESS : kinds.DANGER}
                >
                  {testResult.message ||
                    translate(
                      testResult.isValid
                        ? 'CloudflareSolverTestSuccess'
                        : 'CloudflareSolverTestFailed'
                    )}
                </Alert>
              ) : null}
            </div>
          </FormGroup>

          <FormGroup advancedSettings={showAdvancedSettings} isAdvanced={true}>
            <FormLabel>{translate('RssSyncInterval')}</FormLabel>

            <FormInputGroup
              type={inputTypes.NUMBER}
              name="rssSyncInterval"
              min={0}
              max={120}
              unit="minutes"
              helpText={translate('RssSyncIntervalHelpText')}
              helpTextWarning={translate('RssSyncIntervalHelpTextWarning')}
              helpLink="https://wiki.servarr.com/sonarr/faq#how-does-sonarr-find-episodes"
              onChange={handleInputChange}
              {...settings.rssSyncInterval}
            />
          </FormGroup>
        </Form>
      ) : null}
    </FieldSet>
  );
}

export default IndexerOptions;
