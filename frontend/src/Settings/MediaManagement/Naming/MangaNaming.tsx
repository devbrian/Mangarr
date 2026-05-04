// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/MediaManagement/Naming/Naming.tsx.
//
// UI-07: Manga Naming FieldSet rendered inside Settings → Media Management page (D-05).
//
// Manga sibling preserves: FieldSet wrapper + Form + FormGroup rows + token-picker (?) buttons +
// rename-toggle gating + child-state-change hook (parent dispatches save).
//
// Manga sibling diverges from Naming:
//   * Wires /api/v5/config/manganaming (Phase 5 Plan 05-06) instead of /config/naming
//   * Form fields: standardChapterFormat (text + token picker), mangaFolderFormat (text + token picker),
//     renameChapters (toggle), replaceIllegalCharacters (toggle), colonReplacementFormat (select)
//   * Presets dropdown wires GET /api/v5/config/manganaming/presets/manga (Komga / Kavita / ComicRack /
//     Custom — Phase 5 D-13..D-16). Selecting a preset patches the standardChapterFormat +
//     mangaFolderFormat fields. Default = Komga (Phase 5 D-16).
//   * Token picker is MangaNamingModal (manga-shape token list — no daily/anime/season/airDate)
//   * Live preview uses client-side token substitution (POST /preview endpoint not in scope this phase)
//
// Phase 8 cleanup: this stays — manga-canonical.

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputButton from 'Components/Form/FormInputButton';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import useModalOpenState from 'Helpers/Hooks/useModalOpenState';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import MangaNamingModal from './MangaNamingModal';
import {
  MangaNamingPreset,
  MangaNamingSettingsModel,
  useManageMangaNamingSettings,
  useMangaNamingPresets,
} from './useMangaNamingSettings';
import styles from './MangaNaming.css';

interface MangaNamingModalOptions {
  name: keyof Pick<
    MangaNamingSettingsModel,
    'standardChapterFormat' | 'mangaFolderFormat'
  >;
  chapter?: boolean;
  additional?: boolean;
}

interface MangaNamingProps {
  setChildSave: (saveCallback: () => void) => void;
  onChildStateChange: (state: {
    isSaving: boolean;
    hasPendingChanges: boolean;
  }) => void;
}

const colonReplacementOptions: EnhancedSelectInputValue<number>[] = [
  { key: 0, get value() { return translate('Delete'); } },
  { key: 1, get value() { return translate('ReplaceWithDash'); } },
  { key: 2, get value() { return translate('ReplaceWithSpaceDash'); } },
  { key: 3, get value() { return translate('ReplaceWithSpaceDashSpace'); } },
  {
    key: 4,
    get value() { return translate('SmartReplace'); },
    get hint() { return translate('SmartReplaceHint'); },
  },
  {
    key: 5,
    get value() { return translate('Custom'); },
    get hint() { return translate('CustomColonReplacementFormatHint'); },
  },
];

function substituteSampleTokens(format: string): string {
  if (!format) {
    return '';
  }

  const replacements: Array<[RegExp, string]> = [
    [/\{Manga[ .]Title\}/g, 'Berserk'],
    [/\{Manga[ .]CleanTitle\}/g, 'Berserk'],
    [/\{Manga TitleFirstCharacter\}/g, 'B'],
    [/\{MangaDexId\}/g, 'a1c7c817'],
    [/\{AniListId\}/g, '30002'],
    [/\{MalId\}/g, '2'],
    [/\{Chapter[ .]Number:0000\}/g, '0132'],
    [/\{Chapter[ .]Number:000\}/g, '132'],
    [/\{Chapter[ .]Number:00\}/g, '132'],
    [/\{Chapter[ .]Number\}/g, '132'],
    [/\{Chapter[ .]Title\}/g, 'The Eclipse'],
    [/\{Chapter CleanTitle\}/g, 'The Eclipse'],
    [/\{ScanlationGroup\}/g, 'Evil-Genius'],
    [/\{Language\}/g, 'en'],
  ];

  return replacements.reduce(
    (acc, [pattern, value]) => acc.replace(pattern, value),
    format
  );
}

function MangaNaming({ setChildSave, onChildStateChange }: MangaNamingProps) {
  const {
    settings,
    updateSetting,
    isFetching,
    error,
    hasSettings,
    hasPendingChanges,
    isSaving,
    saveSettings,
  } = useManageMangaNamingSettings();

  const { presets, isPresetsFetching } = useMangaNamingPresets();

  const [isNamingModalOpen, setNamingModalOpen, setNamingModalClosed] =
    useModalOpenState(false);
  const [namingModalOptions, setNamingModalOptions] =
    useState<MangaNamingModalOptions | null>(null);
  const [selectedPreset, setSelectedPreset] = useState<string>('');

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      const key = change.name as keyof MangaNamingSettingsModel;

      updateSetting(
        key,
        change.value as MangaNamingSettingsModel[typeof key]
      );
      // Manual edits clear the active preset selection.
      setSelectedPreset('');
    },
    [updateSetting]
  );

  const handleStandardChapterFormatModalOpenClick = useCallback(() => {
    setNamingModalOpen();
    setNamingModalOptions({
      name: 'standardChapterFormat',
      chapter: true,
      additional: true,
    });
  }, [setNamingModalOpen]);

  const handleMangaFolderFormatModalOpenClick = useCallback(() => {
    setNamingModalOpen();
    setNamingModalOptions({
      name: 'mangaFolderFormat',
    });
  }, [setNamingModalOpen]);

  const presetOptions: EnhancedSelectInputValue<string>[] = useMemo(() => {
    return [
      {
        key: '',
        value: translate('SelectPreset'),
      },
      ...presets.map((p: MangaNamingPreset) => ({
        key: p.name,
        value: p.name,
        hint: p.description,
      })),
    ];
  }, [presets]);

  const handlePresetChange = useCallback(
    ({ value }: { value: string }) => {
      setSelectedPreset(value);
      const preset = presets.find((p) => p.name === value);
      if (preset) {
        updateSetting('standardChapterFormat', preset.standardChapterFormat);
        updateSetting('mangaFolderFormat', preset.mangaFolderFormat);
      }
    },
    [presets, updateSetting]
  );

  const renameChapters = hasSettings && settings.renameChapters.value;
  const replaceIllegalCharacters =
    hasSettings && settings.replaceIllegalCharacters.value;

  const standardChapterFormatHelpTexts: string[] = [];
  const mangaFolderFormatHelpTexts: string[] = [];

  if (hasSettings) {
    const chapterPreview = substituteSampleTokens(
      settings.standardChapterFormat.value
    );
    if (chapterPreview) {
      standardChapterFormatHelpTexts.push(
        `${translate('Example')}: ${chapterPreview}`
      );
    }

    const folderPreview = substituteSampleTokens(
      settings.mangaFolderFormat.value
    );
    if (folderPreview) {
      mangaFolderFormatHelpTexts.push(
        `${translate('Example')}: ${folderPreview}`
      );
    }
  }

  useEffect(() => {
    onChildStateChange({
      hasPendingChanges,
      isSaving,
    });
  }, [hasPendingChanges, isSaving, onChildStateChange]);

  useEffect(() => {
    setChildSave(saveSettings);
  }, [setChildSave, saveSettings]);

  return (
    <FieldSet legend={translate('MangaNaming')}>
      {isFetching || isPresetsFetching ? <LoadingIndicator /> : null}

      {!isFetching && error ? (
        <Alert kind={kinds.DANGER}>
          {translate('MangaNamingSettingsLoadError')}
        </Alert>
      ) : null}

      {hasSettings && !isFetching && !error ? (
        <Form>
          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('NamingPreset')}</FormLabel>

            <FormInputGroup
              type={inputTypes.SELECT}
              name="namingPreset"
              value={selectedPreset}
              values={presetOptions}
              helpText={translate('MangaNamingPresetHelpText')}
              onChange={handlePresetChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('RenameChapters')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="renameChapters"
              helpText={translate('RenameChaptersHelpText')}
              onChange={handleInputChange}
              {...settings.renameChapters}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('ReplaceIllegalCharacters')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="replaceIllegalCharacters"
              helpText={translate('ReplaceIllegalCharactersHelpText')}
              onChange={handleInputChange}
              {...settings.replaceIllegalCharacters}
            />
          </FormGroup>

          {replaceIllegalCharacters ? (
            <FormGroup size={sizes.MEDIUM}>
              <FormLabel>{translate('ColonReplacement')}</FormLabel>

              <FormInputGroup
                type={inputTypes.SELECT}
                name="colonReplacementFormat"
                values={colonReplacementOptions}
                helpText={translate('ColonReplacementFormatHelpText')}
                onChange={handleInputChange}
                {...settings.colonReplacementFormat}
              />
            </FormGroup>
          ) : null}

          {replaceIllegalCharacters &&
          settings.colonReplacementFormat.value === 5 ? (
            <FormGroup size={sizes.MEDIUM}>
              <FormLabel>{translate('CustomColonReplacement')}</FormLabel>

              <FormInputGroup
                type={inputTypes.TEXT}
                name="customColonReplacementFormat"
                helpText={translate('CustomColonReplacementFormatHelpText')}
                onChange={handleInputChange}
                {...settings.customColonReplacementFormat}
              />
            </FormGroup>
          ) : null}

          {renameChapters ? (
            <FormGroup size={sizes.LARGE}>
              <FormLabel>{translate('StandardChapterFormat')}</FormLabel>

              <FormInputGroup
                inputClassName={styles.namingInput}
                type={inputTypes.TEXT}
                name="standardChapterFormat"
                buttons={
                  <FormInputButton
                    onPress={handleStandardChapterFormatModalOpenClick}
                  >
                    ?
                  </FormInputButton>
                }
                onChange={handleInputChange}
                {...settings.standardChapterFormat}
                helpTexts={standardChapterFormatHelpTexts}
              />
            </FormGroup>
          ) : null}

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('MangaFolderFormat')}</FormLabel>

            <FormInputGroup
              inputClassName={styles.namingInput}
              type={inputTypes.TEXT}
              name="mangaFolderFormat"
              buttons={
                <FormInputButton
                  onPress={handleMangaFolderFormatModalOpenClick}
                >
                  ?
                </FormInputButton>
              }
              onChange={handleInputChange}
              {...settings.mangaFolderFormat}
              helpTexts={[
                translate('MangaFolderFormatHelpText'),
                ...mangaFolderFormatHelpTexts,
              ]}
            />
          </FormGroup>

          {namingModalOptions ? (
            <MangaNamingModal
              isOpen={isNamingModalOpen}
              {...namingModalOptions}
              value={settings[namingModalOptions.name].value}
              onInputChange={handleInputChange}
              onModalClose={setNamingModalClosed}
            />
          ) : null}
        </Form>
      ) : null}
    </FieldSet>
  );
}

export default MangaNaming;
