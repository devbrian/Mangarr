import React, { useCallback } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import {
  setMangaTableOptions,
  useMangaTableOptions,
} from 'Manga/mangaOptionsStore';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';

function MangaIndexTableOptions() {
  const { showBanners, showSearchAction } = useMangaTableOptions();

  const handleTableOptionChange = useCallback(
    ({ name, value }: InputChanged<boolean>) => {
      setMangaTableOptions({
        [name]: value,
      });
    },
    []
  );

  return (
    <>
      <FormGroup>
        <FormLabel>{translate('ShowBanners')}</FormLabel>

        <FormInputGroup
          type={inputTypes.CHECK}
          name="showBanners"
          value={showBanners}
          helpText={translate('ShowBannersHelpText')}
          onChange={handleTableOptionChange}
        />
      </FormGroup>

      <FormGroup>
        <FormLabel>{translate('ShowSearch')}</FormLabel>

        <FormInputGroup
          type={inputTypes.CHECK}
          name="showSearchAction"
          value={showSearchAction}
          helpText={translate('ShowSearchHelpText')}
          onChange={handleTableOptionChange}
        />
      </FormGroup>
    </>
  );
}

export default MangaIndexTableOptions;
