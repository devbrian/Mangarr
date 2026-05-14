import { orderBy } from 'lodash';
import React, { useCallback, useMemo } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons, kinds } from 'Helpers/Props';
import Manga from 'Manga/Manga';
import useManga from 'Manga/useManga';
import translate from 'Utilities/String/translate';
import styles from './OrganizeMangaModalContent.css';

export interface OrganizeMangaModalContentProps {
  onModalClose: () => void;
}

function OrganizeMangaModalContent({
  onModalClose,
}: OrganizeMangaModalContentProps) {
  const { data: allSeries } = useManga();
  const executeCommand = useExecuteCommand();
  const { useSelectedIds } = useSelect<Manga>();
  const mangaIds = useSelectedIds();

  const seriesTitles = useMemo(() => {
    const manga = mangaIds.reduce((acc: Manga[], id) => {
      const s = allSeries.find((s) => s.id === id);

      if (s) {
        acc.push(s);
      }

      return acc;
    }, []);

    const sorted = orderBy(manga, ['sortTitle']);

    return sorted.map((s) => s.title);
  }, [allSeries, mangaIds]);

  const onOrganizePress = useCallback(() => {
    executeCommand({
      name: CommandNames.RenameSeries,
      mangaIds,
    });

    onModalClose();
  }, [mangaIds, onModalClose, executeCommand]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('OrganizeSelectedMangaModalHeader')}</ModalHeader>

      <ModalBody>
        <Alert>
          {translate('OrganizeSelectedMangaModalAlert')}
          <Icon className={styles.renameIcon} name={icons.ORGANIZE} />
        </Alert>

        <div className={styles.message}>
          {translate('OrganizeSelectedMangaModalConfirmation', {
            count: seriesTitles.length,
          })}
        </div>

        <ul>
          {seriesTitles.map((title) => {
            return <li key={title}>{title}</li>;
          })}
        </ul>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <Button kind={kinds.DANGER} onPress={onOrganizePress}>
          {translate('Organize')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default OrganizeMangaModalContent;
