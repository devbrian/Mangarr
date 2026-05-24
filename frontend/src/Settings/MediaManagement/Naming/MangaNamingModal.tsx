// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/MediaManagement/Naming/NamingModal.tsx.
//
// UI-07: Token-picker modal for the manga naming format inputs (StandardChapterFormat / MangaFolderFormat).
//
// Manga sibling preserves: Modal + ModalContent + ModalHeader + ModalBody + ModalFooter scaffold;
// SelectInput separator + case picker; FieldSet groups of NamingOption tokens; ModalFooter TextInput
// + Close button row.
//
// Manga sibling diverges from NamingModal:
//   * Token list is manga-shaped (Manga.Title / Manga.MalId / Chapter.Number / ScanlationGroup / Language)
//     instead of TV-shaped (Series TitleYear / season / episode / Quality / MediaInfo)
//   * No daily/anime/season/airDate sections — manga is a flat chapter resource
//   * Reuses existing NamingOption + TokenCase + TokenSeparator primitives unchanged
//
// Phase 8 cleanup: this stays — manga-canonical.

import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import SelectInput, { SelectInputOption } from 'Components/Form/SelectInput';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import NamingOption from './NamingOption';
import TokenCase from './TokenCase';
import TokenSeparator from './TokenSeparator';
import { MangaNamingSettingsModel } from './useMangaNamingSettings';
import styles from './MangaNamingModal.css';

type SeparatorInputOption = Omit<SelectInputOption, 'key'> & {
  key: TokenSeparator;
};

type CaseInputOption = Omit<SelectInputOption, 'key'> & {
  key: TokenCase;
};

const separatorOptions: SeparatorInputOption[] = [
  {
    key: ' ',
    get value() {
      return `${translate('Space')} ( )`;
    },
  },
  {
    key: '.',
    get value() {
      return `${translate('Period')} (.)`;
    },
  },
  {
    key: '_',
    get value() {
      return `${translate('Underscore')} (_)`;
    },
  },
  {
    key: '-',
    get value() {
      return `${translate('Dash')} (-)`;
    },
  },
];

const caseOptions: CaseInputOption[] = [
  {
    key: 'title',
    get value() {
      return translate('DefaultCase');
    },
  },
  {
    key: 'lower',
    get value() {
      return translate('Lowercase');
    },
  },
  {
    key: 'upper',
    get value() {
      return translate('Uppercase');
    },
  },
];

// Locked per Phase 5 D-13..D-16 (RESEARCH Pattern 5 reader-compat tokens).
const fileNameTokens = [
  {
    token: '{Manga.Title} - Chapter {Chapter.Number:000}',
    example: 'Berserk - Chapter 132',
  },
  {
    token: '{Manga.Title} Ch.{Chapter.Number:0000}',
    example: 'Berserk Ch.0132',
  },
  {
    token: '{Manga.Title} #{Chapter.Number:000}',
    example: 'Berserk #132',
  },
  {
    token: '{Manga.Title} - Chapter {Chapter.Number:000} - {Chapter.Title}',
    example: 'Berserk - Chapter 132 - The Eclipse',
  },
];

const mangaTokens = [
  { token: '{Manga Title}', example: 'Berserk' },
  { token: '{Manga.Title}', example: 'Berserk' },
  { token: '{Manga CleanTitle}', example: 'Berserk' },
  { token: '{Manga TitleFirstCharacter}', example: 'B' },
];

const mangaIdTokens = [
  { token: '{MangaDexId}', example: 'a1c7c817' },
  { token: '{AniListId}', example: '30002' },
  { token: '{MalId}', example: '2' },
];

const chapterTokens = [
  { token: '{Chapter.Number}', example: '132' },
  { token: '{Chapter.Number:00}', example: '132' },
  { token: '{Chapter.Number:000}', example: '132' },
  { token: '{Chapter.Number:0000}', example: '0132' },
];

const chapterTitleTokens = [
  { token: '{Chapter Title}', example: 'The Eclipse' },
  { token: '{Chapter.Title}', example: 'The.Eclipse' },
  { token: '{Chapter CleanTitle}', example: 'The Eclipse' },
];

const releaseTokens = [
  { token: '{ScanlationGroup}', example: 'Evil-Genius' },
  { token: '{Language}', example: 'en' },
];

// Phase 30 Plan 30-05 (II2-03) — MediaInfo-backed naming tokens. Populated by
// the ImageSharp probe at ImportApprovedChapters step 3.5. D-09 null-skip: each
// token renders empty when the relevant subfield is null (e.g., DPI is null for
// scans without EXIF metadata; pre-Migration-004 files stay null entirely).
const mediaInfoTokens = [
  { token: '{Page Count}', example: '24' },
  { token: '{Color}', example: 'Color' },
  { token: '{DPI}', example: '300' },
];

interface MangaNamingModalProps {
  isOpen: boolean;
  name: keyof Pick<
    MangaNamingSettingsModel,
    'standardChapterFormat' | 'mangaFolderFormat'
  >;
  value: string;
  chapter?: boolean;
  additional?: boolean;
  onInputChange: ({ name, value }: { name: string; value: string }) => void;
  onModalClose: () => void;
}

function MangaNamingModal(props: MangaNamingModalProps) {
  const {
    isOpen,
    name,
    value,
    chapter = false,
    additional = false,
    onInputChange,
    onModalClose,
  } = props;

  const [tokenSeparator, setTokenSeparator] = useState<TokenSeparator>(' ');
  const [tokenCase, setTokenCase] = useState<TokenCase>('title');
  const [selectionStart, setSelectionStart] = useState<number | null>(null);
  const [selectionEnd, setSelectionEnd] = useState<number | null>(null);

  const handleTokenSeparatorChange = useCallback(
    ({ value }: { value: TokenSeparator }) => {
      setTokenSeparator(value);
    },
    [setTokenSeparator]
  );

  const handleTokenCaseChange = useCallback(
    ({ value }: { value: TokenCase }) => {
      setTokenCase(value);
    },
    [setTokenCase]
  );

  const handleInputSelectionChange = useCallback(
    (selectionStart: number | null, selectionEnd: number | null) => {
      setSelectionStart(selectionStart);
      setSelectionEnd(selectionEnd);
    },
    [setSelectionStart, setSelectionEnd]
  );

  const handleOptionPress = useCallback(
    ({
      isFullFilename,
      tokenValue,
    }: {
      isFullFilename: boolean;
      tokenValue: string;
    }) => {
      if (isFullFilename) {
        onInputChange({ name, value: tokenValue });
      } else if (selectionStart == null || selectionEnd == null) {
        onInputChange({
          name,
          value: `${value}${tokenValue}`,
        });
      } else {
        const start = value.substring(0, selectionStart);
        const end = value.substring(selectionEnd);
        const newValue = `${start}${tokenValue}${end}`;

        onInputChange({ name, value: newValue });

        setSelectionStart(newValue.length - 1);
        setSelectionEnd(newValue.length - 1);
      }
    },
    [name, value, selectionEnd, selectionStart, onInputChange]
  );

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {chapter
            ? translate('FileNameTokens')
            : translate('FolderNameTokens')}
        </ModalHeader>

        <ModalBody>
          <div className={styles.namingSelectContainer}>
            <SelectInput
              className={styles.namingSelect}
              name="separator"
              value={tokenSeparator}
              values={separatorOptions}
              onChange={handleTokenSeparatorChange}
            />

            <SelectInput
              className={styles.namingSelect}
              name="case"
              value={tokenCase}
              values={caseOptions}
              onChange={handleTokenCaseChange}
            />
          </div>

          {chapter ? (
            <FieldSet legend={translate('FileNames')}>
              <div className={styles.groups}>
                {fileNameTokens.map(({ token, example }) => (
                  <NamingOption
                    key={token}
                    token={token}
                    example={example}
                    isFullFilename={true}
                    tokenSeparator={tokenSeparator}
                    tokenCase={tokenCase}
                    size={sizes.LARGE}
                    onPress={handleOptionPress}
                  />
                ))}
              </div>
            </FieldSet>
          ) : null}

          <FieldSet legend={translate('Manga')}>
            <div className={styles.groups}>
              {mangaTokens.map(({ token, example }) => (
                <NamingOption
                  key={token}
                  token={token}
                  example={example}
                  tokenSeparator={tokenSeparator}
                  tokenCase={tokenCase}
                  onPress={handleOptionPress}
                />
              ))}
            </div>
          </FieldSet>

          <FieldSet legend={translate('MangaID')}>
            <div className={styles.groups}>
              {mangaIdTokens.map(({ token, example }) => (
                <NamingOption
                  key={token}
                  token={token}
                  example={example}
                  tokenSeparator={tokenSeparator}
                  tokenCase={tokenCase}
                  onPress={handleOptionPress}
                />
              ))}
            </div>
          </FieldSet>

          {chapter ? (
            <FieldSet legend={translate('Chapter')}>
              <div className={styles.groups}>
                {chapterTokens.map(({ token, example }) => (
                  <NamingOption
                    key={token}
                    token={token}
                    example={example}
                    tokenSeparator={tokenSeparator}
                    tokenCase={tokenCase}
                    onPress={handleOptionPress}
                  />
                ))}
              </div>
            </FieldSet>
          ) : null}

          {additional ? (
            <div>
              <FieldSet legend={translate('ChapterTitle')}>
                <div className={styles.groups}>
                  {chapterTitleTokens.map(({ token, example }) => (
                    <NamingOption
                      key={token}
                      token={token}
                      example={example}
                      tokenSeparator={tokenSeparator}
                      tokenCase={tokenCase}
                      onPress={handleOptionPress}
                    />
                  ))}
                </div>
              </FieldSet>

              <FieldSet legend={translate('Release')}>
                <div className={styles.groups}>
                  {releaseTokens.map(({ token, example }) => (
                    <NamingOption
                      key={token}
                      token={token}
                      example={example}
                      tokenSeparator={tokenSeparator}
                      tokenCase={tokenCase}
                      onPress={handleOptionPress}
                    />
                  ))}
                </div>
              </FieldSet>

              {/* Phase 30 Plan 30-05 (II2-03) — MediaInfo-backed tokens (probe result). */}
              <FieldSet legend={translate('MediaInfo')}>
                <div className={styles.groups}>
                  {mediaInfoTokens.map(({ token, example }) => (
                    <NamingOption
                      key={token}
                      token={token}
                      example={example}
                      tokenSeparator={tokenSeparator}
                      tokenCase={tokenCase}
                      onPress={handleOptionPress}
                    />
                  ))}
                </div>
              </FieldSet>
            </div>
          ) : null}
        </ModalBody>

        <ModalFooter>
          <TextInput
            name={name}
            value={value}
            onChange={onInputChange}
            onSelectionChange={handleInputSelectionChange}
          />

          <Button onPress={onModalClose}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default MangaNamingModal;
