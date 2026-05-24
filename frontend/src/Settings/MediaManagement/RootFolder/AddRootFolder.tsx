import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import FileBrowserModal from 'Components/FileBrowser/FileBrowserModal';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import { icons, kinds, sizes } from 'Helpers/Props';
import { useAddRootFolder } from 'RootFolder/useRootFolders';
import translate from 'Utilities/String/translate';
import styles from './AddRootFolder.css';

function AddRootFolder() {
  const { addRootFolder, isAdding, addError } = useAddRootFolder();

  const [isAddNewRootFolderModalOpen, setIsAddNewRootFolderModalOpen] =
    useState(false);

  const onAddNewRootFolderPress = useCallback(() => {
    setIsAddNewRootFolderModalOpen(true);
  }, [setIsAddNewRootFolderModalOpen]);

  const onNewRootFolderSelect = useCallback(
    ({ value }: { value: string }) => {
      addRootFolder({ path: value });
    },
    [addRootFolder]
  );

  const onAddRootFolderModalClose = useCallback(() => {
    setIsAddNewRootFolderModalOpen(false);
  }, [setIsAddNewRootFolderModalOpen]);

  return (
    <>
      {!isAdding && addError ? (
        <Alert kind={kinds.DANGER}>
          {translate('AddRootFolderError')}

          <ul>
            {Array.isArray(addError.statusBody) ? (
              addError.statusBody.map((e, index) => {
                return <li key={index}>{e.errorMessage}</li>;
              })
            ) : (
              <li>{JSON.stringify(addError.statusBody)}</li>
            )}
          </ul>
        </Alert>
      ) : null}

      <div className={styles.addRootFolderButtonContainer}>
        {/* Phase 18 Plan 18-07: D-08 catalog item — SettingsFlow.AddRootFolderAsync
            uses this button as the entry point. Button extends Link, which propagates
            data-testid via ...otherProps (data-testid-spec Wrapper-Component Sweep Ledger). */}
        <Button
          kind={kinds.PRIMARY}
          size={sizes.LARGE}
          data-testid="settings-root-folders-add-button"
          onPress={onAddNewRootFolderPress}
        >
          <Icon className={styles.importButtonIcon} name={icons.DRIVE} />
          {translate('AddRootFolder')}
        </Button>

        {/* Phase 30 Plan 30-02 (II2-08) Strategy A — wrap the modal in a labeled,
            conditionally-rendered div so SettingsFlow.AddRootFolderAsync can detect
            the modal-open state without modifying the shared FileBrowserModal infra
            (which would collide with EditManga + InteractiveImport consumers per R-9).
            The wrapper itself is empty in the rendered DOM because Modal portals to
            #modal-root; the testid still ships into the React tree so Playwright can
            wait on it as the open-state indicator. Inner path-input + Ok button are
            reached via role-based selectors against the portal'd <div role="dialog">. */}
        {isAddNewRootFolderModalOpen ? (
          <div data-testid="add-root-folder-modal">
            <FileBrowserModal
              isOpen={isAddNewRootFolderModalOpen}
              name="rootFolderPath"
              value=""
              onChange={onNewRootFolderSelect}
              onModalClose={onAddRootFolderModalClose}
            />
          </div>
        ) : null}
      </div>
    </>
  );
}

export default AddRootFolder;
