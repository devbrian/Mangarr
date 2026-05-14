// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/Profiles/Quality/EditQualityProfileModalContent.tsx (lines 1-749).
//
// UI-07: Edit modal CONTENT for the CustomFormatProfile editor (Phase 5 Plan 05-03 entity per D-07).
//
// Manga sibling preserves: Form / FormGroup / FormInputGroup scaffold; SpinnerErrorButton save flow;
// ModalContent + ModalHeader + ModalBody + ModalFooter layout; auto-height measure pattern.
//
// Manga sibling diverges from EditQualityProfileModalContent:
//   * No schema fetch — CustomFormatProfile is a flat shape
//   * formatItems list (Phase 5 D-07) — each row has customFormatId dropdown + score numeric input
//   * Available CustomFormats fetched via /api/v5/customformat?mediaType=manga (Phase 5 D-10)
//   * minFormatScore / maxFormatScore numeric range pickers
//   * upgradeAllowed Toggle (Phase 5 D-10 / Phase 6 D-10 — three-state effective-upgrade-allowed)
//   * Save button label uses i18n key 'SaveCustomFormatProfile' per UI-SPEC §Page-level CTAs
//
// Phase 8 cleanup: this stays — manga-canonical.

import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import IconButton from 'Components/Link/IconButton';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import useMeasure from 'Helpers/Hooks/useMeasure';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { icons, inputTypes, kinds, sizes } from 'Helpers/Props';
import useManga from 'Manga/useManga';
import translate from 'Utilities/String/translate';
import { CustomFormatProfileResource } from './CustomFormatProfile';

const PATH = '/customformatprofile';
// Phase 5 D-10: filter Custom Formats by mediaType=manga so the dropdown surfaces only manga-relevant specs.
const CUSTOM_FORMAT_PATH = '/customformat?mediaType=manga';

interface CustomFormatResource {
  id: number;
  name: string;
}

interface InputChangedHandler<T> {
  name: string;
  value: T;
}

interface NumberInputChangedLocal {
  name: string;
  value: number | null;
}

interface EditCustomFormatProfileModalContentProps {
  id?: number;
  onContentHeightChange: (height: number) => void;
  onModalClose: () => void;
}

function defaultProfile(): CustomFormatProfileResource {
  return {
    id: 0,
    name: '',
    isDefault: false,
    formatItems: [],
    minFormatScore: 0,
    maxFormatScore: 0,
    upgradeAllowed: false,
  };
}

// Per-row sub-component for the formatItems list. Extracted so the parent's
// .map() does not allocate fresh arrow handlers per render (react/jsx-no-bind);
// each row binds its own idx into stable useCallback handlers here.
interface FormatItemRowProps {
  idx: number;
  formatItem: CustomFormatProfileResource['formatItems'][number];
  customFormatValues: { key: string; value: string }[];
  onFormatChange: (idx: number, customFormatId: number) => void;
  onScoreChange: (idx: number, score: number) => void;
  onRemove: (idx: number) => void;
}

function FormatItemRow({
  idx,
  formatItem,
  customFormatValues,
  onFormatChange,
  onScoreChange,
  onRemove,
}: FormatItemRowProps) {
  const handleFormatChange = useCallback(
    (c: InputChangedHandler<string>) => {
      onFormatChange(idx, Number(c.value));
    },
    [idx, onFormatChange]
  );

  const handleScoreChange = useCallback(
    (c: NumberInputChangedLocal) => {
      onScoreChange(idx, c.value ?? 0);
    },
    [idx, onScoreChange]
  );

  const handleRemove = useCallback(() => {
    onRemove(idx);
  }, [idx, onRemove]);

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        marginBottom: 6,
        gap: 6,
      }}
    >
      <div style={{ minWidth: 200 }}>
        <FormInputGroup
          type={inputTypes.SELECT}
          name={`customFormatId-${idx}`}
          value={String(formatItem.customFormatId)}
          values={customFormatValues}
          onChange={handleFormatChange}
        />
      </div>
      <div style={{ minWidth: 100 }}>
        <FormInputGroup
          type={inputTypes.NUMBER}
          name={`score-${idx}`}
          value={formatItem.score}
          onChange={handleScoreChange}
        />
      </div>
      <IconButton
        name={icons.REMOVE}
        title={translate('Remove')}
        onPress={handleRemove}
      />
    </div>
  );
}

function EditCustomFormatProfileModalContent({
  id,
  onContentHeightChange,
  onModalClose,
}: EditCustomFormatProfileModalContentProps) {
  const queryClient = useQueryClient();

  // Profile list to find the existing profile when editing.
  const { data: profiles, isFetching: isListFetching } = useApiQuery<
    CustomFormatProfileResource[]
  >({
    path: PATH,
    queryOptions: { gcTime: Infinity, staleTime: 5 * 60 * 1000 },
  });

  // Available manga-relevant Custom Formats for the formatItems dropdown (Phase 5 D-10).
  const { data: customFormats = [] } = useApiQuery<CustomFormatResource[]>({
    path: CUSTOM_FORMAT_PATH,
    queryOptions: { gcTime: Infinity, staleTime: 5 * 60 * 1000 },
  });

  const customFormatValues = useMemo(
    () =>
      customFormats.map((cf) => ({
        key: String(cf.id),
        value: cf.name,
      })),
    [customFormats]
  );

  const existing = useMemo(() => {
    if (id === undefined || id === 0 || !profiles) {
      return undefined;
    }
    return profiles.find((p) => p.id === id);
  }, [id, profiles]);

  const [item, setItem] = useState<CustomFormatProfileResource>(
    () => existing ?? defaultProfile()
  );

  useEffect(() => {
    if (existing) {
      setItem(existing);
    }
  }, [existing]);

  // In-use count (Manga referencing this profile).
  const { data: mangaList = [] } = useManga();
  const inUseCount = useMemo(() => {
    if (id === undefined || id === 0) {
      return 0;
    }
    return mangaList.filter(
      (m: { customFormatProfileId?: number }) => m.customFormatProfileId === id
    ).length;
  }, [id, mangaList]);
  const isInUse = inUseCount > 0;

  const isEditing = id !== undefined && id !== 0;

  // Save mutation
  const {
    mutate: save,
    isPending: isSaving,
    error: saveError,
  } = useApiMutation<CustomFormatProfileResource, CustomFormatProfileResource>({
    path: isEditing ? `${PATH}/${id}` : PATH,
    method: isEditing ? 'PUT' : 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [PATH] });
      },
    },
  });

  // Delete mutation
  const {
    mutate: deleteProfile,
    isPending: isDeleting,
    error: deleteError,
  } = useApiMutation<void, void>({
    path: `${PATH}/${id ?? 0}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [PATH] });
      },
    },
  });

  const [isDeleteConfirmOpen, setIsDeleteConfirmOpen] = useState(false);

  const wasSaving = usePrevious(isSaving);
  const wasDeleting = usePrevious(isDeleting);

  useEffect(() => {
    if (wasSaving && !isSaving && !saveError) {
      onModalClose();
    }
  }, [isSaving, wasSaving, saveError, onModalClose]);

  useEffect(() => {
    if (wasDeleting && !isDeleting && !deleteError) {
      onModalClose();
    }
  }, [isDeleting, wasDeleting, deleteError, onModalClose]);

  // Height measurement
  const [measureHeaderRef, { height: headerHeight }] = useMeasure();
  const [measureBodyRef, { height: bodyHeight }] = useMeasure();
  const [measureFooterRef, { height: footerHeight }] = useMeasure();

  useEffect(() => {
    onContentHeightChange(headerHeight + bodyHeight + footerHeight + 40);
  }, [headerHeight, bodyHeight, footerHeight, onContentHeightChange]);

  // Field handlers
  const handleNameChange = useCallback(
    (change: InputChangedHandler<string>) => {
      setItem((prev) => ({ ...prev, name: change.value }));
    },
    []
  );

  const handleIsDefaultChange = useCallback(
    (change: InputChangedHandler<boolean>) => {
      setItem((prev) => ({ ...prev, isDefault: change.value }));
    },
    []
  );

  const handleUpgradeAllowedChange = useCallback(
    (change: InputChangedHandler<boolean>) => {
      setItem((prev) => ({ ...prev, upgradeAllowed: change.value }));
    },
    []
  );

  const handleMinFormatScoreChange = useCallback(
    (change: NumberInputChangedLocal) => {
      setItem((prev) => ({ ...prev, minFormatScore: change.value ?? 0 }));
    },
    []
  );

  const handleMaxFormatScoreChange = useCallback(
    (change: NumberInputChangedLocal) => {
      setItem((prev) => ({ ...prev, maxFormatScore: change.value ?? 0 }));
    },
    []
  );

  const handleFormatItemFormatChange = useCallback(
    (idx: number, customFormatId: number) => {
      setItem((prev) => ({
        ...prev,
        formatItems: prev.formatItems.map((fi, i) =>
          i === idx ? { ...fi, customFormatId } : fi
        ),
      }));
    },
    []
  );

  const handleFormatItemScoreChange = useCallback(
    (idx: number, score: number) => {
      setItem((prev) => ({
        ...prev,
        formatItems: prev.formatItems.map((fi, i) =>
          i === idx ? { ...fi, score } : fi
        ),
      }));
    },
    []
  );

  const handleAddFormatItem = useCallback(() => {
    const firstAvailable = customFormats[0]?.id ?? 0;
    setItem((prev) => ({
      ...prev,
      formatItems: [
        ...prev.formatItems,
        { customFormatId: firstAvailable, score: 0 },
      ],
    }));
  }, [customFormats]);

  const handleRemoveFormatItem = useCallback((idx: number) => {
    setItem((prev) => ({
      ...prev,
      formatItems: prev.formatItems.filter((_, i) => i !== idx),
    }));
  }, []);

  const handleSavePress = useCallback(() => {
    save(item);
  }, [save, item]);

  const handleDeletePress = useCallback(() => {
    setIsDeleteConfirmOpen(true);
  }, []);

  const handleConfirmDelete = useCallback(() => {
    deleteProfile();
    setIsDeleteConfirmOpen(false);
  }, [deleteProfile]);

  const handleCancelDelete = useCallback(() => {
    setIsDeleteConfirmOpen(false);
  }, []);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader ref={measureHeaderRef}>
        {isEditing
          ? translate('EditCustomFormatProfile')
          : translate('AddCustomFormatProfile')}
      </ModalHeader>

      <ModalBody>
        <div ref={measureBodyRef}>
          {isListFetching && isEditing && !existing ? (
            <LoadingIndicator />
          ) : (
            <Form>
              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>{translate('Name')}</FormLabel>
                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="name"
                  value={item.name}
                  onChange={handleNameChange}
                />
              </FormGroup>

              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>
                  {translate('DefaultProfile')}
                </FormLabel>
                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="isDefault"
                  value={item.isDefault}
                  onChange={handleIsDefaultChange}
                />
              </FormGroup>

              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>
                  {translate('UpgradesAllowed')}
                </FormLabel>
                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="upgradeAllowed"
                  value={item.upgradeAllowed}
                  helpText={translate('UpgradesAllowedHelpText')}
                  onChange={handleUpgradeAllowedChange}
                />
              </FormGroup>

              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>
                  {translate('MinimumCustomFormatScore')}
                </FormLabel>
                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="minFormatScore"
                  value={item.minFormatScore}
                  helpText={translate('MinimumCustomFormatScoreHelpText')}
                  onChange={handleMinFormatScoreChange}
                />
              </FormGroup>

              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>
                  {translate('MaximumCustomFormatScore')}
                </FormLabel>
                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="maxFormatScore"
                  value={item.maxFormatScore}
                  helpText={translate('MaximumCustomFormatScoreHelpText')}
                  onChange={handleMaxFormatScoreChange}
                />
              </FormGroup>

              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>
                  {translate('FormatItems')}
                </FormLabel>
                <div>
                  {item.formatItems.map((fi, idx) => (
                    <FormatItemRow
                      key={idx}
                      idx={idx}
                      formatItem={fi}
                      customFormatValues={customFormatValues}
                      onFormatChange={handleFormatItemFormatChange}
                      onScoreChange={handleFormatItemScoreChange}
                      onRemove={handleRemoveFormatItem}
                    />
                  ))}
                  <Button
                    isDisabled={customFormats.length === 0}
                    onPress={handleAddFormatItem}
                  >
                    {translate('AddFormatItem')}
                  </Button>
                  {customFormats.length === 0 ? (
                    <div style={{ marginTop: 6 }}>
                      <Alert kind={kinds.INFO}>
                        {translate('NoMangaCustomFormatsAvailable')}
                      </Alert>
                    </div>
                  ) : null}
                </div>
              </FormGroup>

              {isInUse && isEditing ? (
                <Alert kind={kinds.WARNING}>
                  {translate('CustomFormatProfileInUseHelpText', {
                    count: inUseCount,
                  })}
                </Alert>
              ) : null}
            </Form>
          )}
        </div>
      </ModalBody>

      <ModalFooter ref={measureFooterRef}>
        {isEditing ? (
          <Button
            kind={kinds.DANGER}
            isDisabled={isInUse}
            onPress={handleDeletePress}
          >
            {translate('DeleteCustomFormatProfile')}
          </Button>
        ) : null}

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          isSpinning={isSaving}
          error={saveError}
          onPress={handleSavePress}
        >
          {translate('SaveCustomFormatProfile')}
        </SpinnerErrorButton>
      </ModalFooter>

      <ConfirmModal
        isOpen={isDeleteConfirmOpen}
        kind={kinds.DANGER}
        title={translate('DeleteCustomFormatProfile')}
        message={translate('DeleteCustomFormatProfileMessageText', {
          name: item.name,
        })}
        confirmLabel={translate('DeleteProfile')}
        isSpinning={isDeleting}
        onConfirm={handleConfirmDelete}
        onCancel={handleCancelDelete}
      />
    </ModalContent>
  );
}

export default EditCustomFormatProfileModalContent;
