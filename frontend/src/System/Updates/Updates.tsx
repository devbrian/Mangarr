import React, { useCallback, useMemo, useState } from 'react';
import { useAppValue } from 'App/appStore';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import ClipboardButton from 'Components/Link/ClipboardButton';
import Link from 'Components/Link/Link';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { icons, kinds } from 'Helpers/Props';
import {
  UpdateMechanism,
  useGeneralSettings,
} from 'Settings/General/useGeneralSettings';
import useUpdateSettings from 'Settings/General/useUpdateSettings';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import { useSystemStatusData } from 'System/Status/useSystemStatus';
import formatDate from 'Utilities/Date/formatDate';
import formatDateTime from 'Utilities/Date/formatDateTime';
import translate from 'Utilities/String/translate';
import UpdateChanges from './UpdateChanges';
import useUpdates from './useUpdates';
import styles from './Updates.css';

const VERSION_REGEX = /\d+\.\d+\.\d+\.\d+/i;

function Updates() {
  const currentVersion = useAppValue('version');
  const { packageUpdateMechanismMessage } = useSystemStatusData();

  const { shortDateFormat, longDateFormat, timeFormat } = useUiSettingsValues();
  const isInstallingUpdate = useCommandExecuting(
    CommandNames.ApplicationUpdate
  );

  const {
    data: updates,
    isFetched: isUpdatesFetched,
    isLoading: isLoadingUpdates,
    error: updatesError,
  } = useUpdates();
  const {
    data: updateSettings,
    isFetched: isSettingsFetched,
    isLoading: isLoadingSettings,
    error: settingsError,
  } = useUpdateSettings();

  const executeCommand = useExecuteCommand();
  const [isMajorUpdateModalOpen, setIsMajorUpdateModalOpen] = useState(false);
  const isFetching = isLoadingUpdates || isLoadingSettings;
  const isPopulated = isUpdatesFetched && isSettingsFetched;
  const updateMechanism = updateSettings?.updateMechanism ?? 'builtIn';
  const hasError = !!(updatesError || settingsError);
  const hasUpdates = isPopulated && !hasError && updates.length > 0;
  const noUpdates = isPopulated && !hasError && !updates.length;

  const externalUpdaterPrefix = translate('UpdateAppDirectlyLoadError');
  const externalUpdaterMessages: Partial<Record<UpdateMechanism, string>> = {
    external: translate('ExternalUpdater'),
    apt: translate('AptUpdater'),
    docker: translate('DockerUpdater'),
  };

  // Phase 29 D-04 (PR #248 review — CodeRabbit Major): select a single candidate
  // for both eligibility AND banner content. The earlier code derived
  // hasUpdateToInstall from `updates.some(u => u.installable && u.latest)` but the
  // banner read every field from `updates[0]`, so a release ordering quirk could
  // surface the wrong version/URL/docker-pull string in the banner.
  const { isMajorUpdate, installableUpdate } = useMemo(() => {
    const majorVersion = parseInt(
      currentVersion.match(VERSION_REGEX)?.[0] ?? '0'
    );

    const latestVersion = updates[0]?.version;
    const latestMajorVersion = parseInt(
      latestVersion?.match(VERSION_REGEX)?.[0] ?? '0'
    );

    return {
      isMajorUpdate: latestMajorVersion > majorVersion,
      installableUpdate: updates.find((u) => u.installable && u.latest) ?? null,
    };
  }, [currentVersion, updates]);

  const hasUpdateToInstall = installableUpdate !== null;
  const noUpdateToInstall = hasUpdates && !hasUpdateToInstall;

  const handleInstallLatestPress = useCallback(() => {
    if (isMajorUpdate) {
      setIsMajorUpdateModalOpen(true);
    } else {
      executeCommand({ name: CommandNames.ApplicationUpdate });
    }
  }, [isMajorUpdate, setIsMajorUpdateModalOpen, executeCommand]);

  const handleInstallLatestMajorVersionPress = useCallback(() => {
    setIsMajorUpdateModalOpen(false);

    executeCommand({
      name: CommandNames.ApplicationUpdate,
      installMajorUpdate: true,
    });
  }, [setIsMajorUpdateModalOpen, executeCommand]);

  const handleCancelMajorVersionPress = useCallback(() => {
    setIsMajorUpdateModalOpen(false);
  }, [setIsMajorUpdateModalOpen]);

  useGeneralSettings();

  return (
    <PageContent title={translate('Updates')}>
      <div data-testid="system-updates-page">
        <PageContentBody>
          {isPopulated || hasError ? null : <LoadingIndicator />}

          {noUpdates ? (
            <Alert kind={kinds.INFO}>
              {translate('NoUpdatesAreAvailable')}
            </Alert>
          ) : null}

          {hasUpdateToInstall && installableUpdate ? (
            <>
              {/*
                Phase 29 D-04 — Update Available banner: docker-pull command +
                copy-to-clipboard + GitHub Release link. Banner is INFORMATIONAL
                only — ROADMAP cross-cutting locks "no auto-update mechanism".
                The existing Install button below stays for built-in / script
                update mechanisms; the banner is the addition.

                PR #248 review (CodeRabbit Major): all fields read from
                `installableUpdate` (the SAME release that satisfied the
                installable && latest predicate above) so banner copy can never
                surface a different release than the one eligibility was derived from.
              */}
              <Alert kind={kinds.INFO}>
                <div>
                  {translate('UpdateAvailableBannerMessage', {
                    version: installableUpdate.version,
                    tagName: installableUpdate.version,
                  })}
                </div>
                <div className={styles.bannerDockerPullRow}>
                  <code className={styles.bannerDockerPullCommand}>
                    {`docker pull ghcr.io/devbrian/mangarr:${installableUpdate.version}`}
                  </code>
                  <ClipboardButton
                    value={`docker pull ghcr.io/devbrian/mangarr:${installableUpdate.version}`}
                  />
                </div>
                {installableUpdate.htmlUrl || installableUpdate.url ? (
                  <div className={styles.bannerReleaseLink}>
                    <Link
                      to={
                        installableUpdate.htmlUrl ??
                        installableUpdate.url ??
                        undefined
                      }
                    >
                      {translate('ViewGitHubRelease')}
                    </Link>
                  </div>
                ) : null}
              </Alert>
              <div className={styles.messageContainer}>
                {updateMechanism === 'builtIn' ||
                updateMechanism === 'script' ? (
                  <SpinnerButton
                    kind={kinds.PRIMARY}
                    isSpinning={isInstallingUpdate}
                    onPress={handleInstallLatestPress}
                  >
                    {translate('InstallLatest')}
                  </SpinnerButton>
                ) : (
                  <>
                    <Icon name={icons.WARNING} kind={kinds.WARNING} size={30} />

                    <div className={styles.message}>
                      {externalUpdaterPrefix}{' '}
                      <InlineMarkdown
                        data={
                          packageUpdateMechanismMessage ||
                          externalUpdaterMessages[updateMechanism] ||
                          externalUpdaterMessages.external
                        }
                      />
                    </div>
                  </>
                )}

                {isFetching ? (
                  <LoadingIndicator className={styles.loading} size={20} />
                ) : null}
              </div>
            </>
          ) : null}

          {noUpdateToInstall && (
            <div className={styles.messageContainer}>
              <Icon
                className={styles.upToDateIcon}
                name={icons.CHECK_CIRCLE}
                size={30}
              />
              <div className={styles.message}>
                {translate('OnLatestVersion')}
              </div>

              {isFetching && (
                <LoadingIndicator className={styles.loading} size={20} />
              )}
            </div>
          )}

          {hasUpdates && (
            <div>
              {updates.map((update) => {
                return (
                  <div key={update.version} className={styles.update}>
                    <div className={styles.info}>
                      <div className={styles.version}>{update.version}</div>
                      <div className={styles.space}>&mdash;</div>
                      <div
                        className={styles.date}
                        title={formatDateTime(
                          update.releaseDate,
                          longDateFormat,
                          timeFormat
                        )}
                      >
                        {formatDate(update.releaseDate, shortDateFormat)}
                      </div>

                      {update.branch === 'main' ? null : (
                        <Label className={styles.label}>{update.branch}</Label>
                      )}

                      {update.version === currentVersion ? (
                        <Label
                          className={styles.label}
                          kind={kinds.SUCCESS}
                          title={formatDateTime(
                            update.installedOn,
                            longDateFormat,
                            timeFormat
                          )}
                        >
                          {translate('CurrentlyInstalled')}
                        </Label>
                      ) : null}

                      {update.version !== currentVersion &&
                      update.installedOn ? (
                        <Label
                          className={styles.label}
                          kind={kinds.INVERSE}
                          title={formatDateTime(
                            update.installedOn,
                            longDateFormat,
                            timeFormat
                          )}
                        >
                          {translate('PreviouslyInstalled')}
                        </Label>
                      ) : null}
                    </div>

                    {update.changes ? (
                      <div>
                        <UpdateChanges
                          title={translate('New')}
                          changes={update.changes.new}
                        />

                        <UpdateChanges
                          title={translate('Fixed')}
                          changes={update.changes.fixed}
                        />
                      </div>
                    ) : (
                      <div>{translate('MaintenanceRelease')}</div>
                    )}
                  </div>
                );
              })}
            </div>
          )}

          {updatesError ? (
            <Alert kind={kinds.WARNING}>
              {translate('FailedToFetchUpdates')}
            </Alert>
          ) : null}

          {settingsError ? (
            <Alert kind={kinds.DANGER}>
              {translate('FailedToFetchSettings')}
            </Alert>
          ) : null}

          <ConfirmModal
            isOpen={isMajorUpdateModalOpen}
            kind={kinds.WARNING}
            title={translate('InstallMajorVersionUpdate')}
            message={
              <div>
                <div>{translate('InstallMajorVersionUpdateMessage')}</div>
                <div>
                  <InlineMarkdown
                    data={translate('InstallMajorVersionUpdateMessageLink', {
                      domain: 'sonarr.tv',
                      url: 'https://sonarr.tv/#downloads',
                    })}
                  />
                </div>
              </div>
            }
            confirmLabel={translate('Install')}
            onConfirm={handleInstallLatestMajorVersionPress}
            onCancel={handleCancelMajorVersionPress}
          />
        </PageContentBody>
      </div>
    </PageContent>
  );
}

export default Updates;
