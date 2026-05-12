import React from 'react';
import HintedSelectInputSelectedValue from './HintedSelectInputSelectedValue';
import { IMangaTypeOption } from './MangaTypeSelectInput';

interface MangaTypeSelectInputSelectedValueProps {
  selectedValue: string;
  values: IMangaTypeOption[];
  format: string;
}
function MangaTypeSelectInputSelectedValue(
  props: MangaTypeSelectInputSelectedValueProps
) {
  const { selectedValue, values, ...otherProps } = props;
  const format = values.find((v) => v.key === selectedValue)?.format;

  return (
    <HintedSelectInputSelectedValue
      {...otherProps}
      selectedValue={selectedValue}
      values={values}
      hint={format}
    />
  );
}

export default MangaTypeSelectInputSelectedValue;
