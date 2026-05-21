import React from 'react';
import { SortDirection } from 'Helpers/Props/sortDirections';

type PropertyFunction<T> = () => T;

// TODO: Convert to generic so `name` can be a type
interface Column {
  name: string;
  label: string | PropertyFunction<string> | React.ReactNode;
  className?: string;
  columnLabel?: string | PropertyFunction<string>;
  isSortable?: boolean;
  fixedSortDirection?: SortDirection;
  isVisible: boolean;
  isModifiable?: boolean;
  // Optional data-testid propagated through Table -> TableHeaderCell -> Link
  // onto the rendered <th> so Playwright fixtures can click headers via
  // Page.GetByTestId("...") instead of brittle CSS/ARIA-attribute locators.
  'data-testid'?: string;
}

export default Column;
