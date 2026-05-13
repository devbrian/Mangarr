// Sonarr divergence: NEW manga peer of upstream
// frontend/src/Series/Edit/RootFolder/RootFolderModalContent.tsx per
// issue #81 — see DIVERGENCE.md. 1:1 port with renames:
//   * seriesId prop -> mangaId
//   * useApiQuery<SeriesFolder>('/series/${seriesId}/folder')
//     -> useApiQuery<MangaFolder>('/manga/${mangaId}/folder')
//     (the Phase 13 Plan 13-05 MangaFolderController endpoint, finally
//     reaching its caller — D-13-04 forward-prophylactic was authored
//     ahead of this consumer).
//   * translate('UpdateSeriesPath') -> translate('UpdateMangaPath')
//
// Single retained divergence: the shared `RootFolderSelectInput` component
// still uses the option key `seriesFolder` for the per-row computed folder
// label. We pass `seriesFolder: data?.folder` for now; a follow-up issue
// tracks the cross-cutting rename to `mangaFolder` across
// RootFolderSelectInput + RootFolderSelectInputOption +
// RootFolderSelectInputSelectedValue + AddManga consumers.
import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { inputTypes } from 'Helpers/Props';
import { useIsWindows } from 'System/Status/useSystemStatus';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';

export interface RootFolderUpdated {
  path: string;
  rootFolderPath: string;
}

export interface RootFolderModalContentProps {
  mangaId: number;
  rootFolderPath: string;
  onSavePress(change: RootFolderUpdated): void;
  onModalClose(): void;
}

interface MangaFolder {
  folder: string;
}

function RootFolderModalContent(props: RootFolderModalContentProps) {
  const { mangaId, onSavePress, onModalClose } = props;
  const isWindows = useIsWindows();

  const [rootFolderPath, setRootFolderPath] = useState(props.rootFolderPath);

  const { isLoading, data } = useApiQuery<MangaFolder>({
    path: `/manga/${mangaId}/folder`,
  });

  const onInputChange = useCallback(({ value }: InputChanged<string>) => {
    setRootFolderPath(value);
  }, []);

  const handleSavePress = useCallback(() => {
    const separator = isWindows ? '\\' : '/';

    onSavePress({
      path: `${rootFolderPath}${separator}${data?.folder}`,
      rootFolderPath,
    });
  }, [rootFolderPath, isWindows, data, onSavePress]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('UpdateMangaPath')}</ModalHeader>

      <ModalBody>
        <FormGroup>
          <FormLabel>{translate('RootFolder')}</FormLabel>

          <FormInputGroup
            type={inputTypes.ROOT_FOLDER_SELECT}
            name="rootFolderPath"
            value={rootFolderPath}
            valueOptions={{
              seriesFolder: data?.folder,
              isWindows,
            }}
            selectedValueOptions={{
              seriesFolder: data?.folder,
              isWindows,
            }}
            helpText={translate('SeriesEditRootFolderHelpText')}
            onChange={onInputChange}
          />
        </FormGroup>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <Button disabled={isLoading || !data?.folder} onPress={handleSavePress}>
          {translate('UpdatePath')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default RootFolderModalContent;
