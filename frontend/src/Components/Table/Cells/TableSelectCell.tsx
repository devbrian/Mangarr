import React, { useCallback, useEffect, useRef } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import { CheckInputChanged } from 'typings/inputs';
import { SelectStateInputProps } from 'typings/props';
import TableRowCell, { TableRowCellProps } from './TableRowCell';
import styles from './TableSelectCell.css';

interface TableSelectCellProps<T extends number | string = number>
  extends Omit<TableRowCellProps, 'id'> {
  className?: string;
  id: T;
  isSelected?: boolean;
  // GH #180 scope B: per-row checkbox testid propagation through the
  // TableSelectCell → CheckInput chain. Lands on the wrapping <label>
  // inside CheckInput (the visible click target) per scope A. The prop
  // is plumbed through the otherProps spread to CheckInput below.
  'data-testid'?: string;
  onSelectedChange: (options: SelectStateInputProps<T>) => void;
}

function TableSelectCell<T extends number | string = number>({
  className = styles.selectCell,
  id,
  isSelected = false,
  onSelectedChange,
  ...otherProps
}: TableSelectCellProps<T>) {
  const initialIsSelected = useRef(isSelected);
  const handleSelectedChange = useRef(onSelectedChange);

  handleSelectedChange.current = onSelectedChange;

  const handleChange = useCallback(
    ({ value, shiftKey }: CheckInputChanged) => {
      onSelectedChange({ id, value, shiftKey });
    },
    [id, onSelectedChange]
  );

  useEffect(() => {
    handleSelectedChange.current({
      id,
      value: initialIsSelected.current,
      shiftKey: false,
    });

    return () => {
      handleSelectedChange.current({ id, value: null, shiftKey: false });
    };
  }, [id]);

  return (
    <TableRowCell className={className}>
      <CheckInput
        className={styles.input}
        name={id.toString()}
        value={isSelected}
        {...otherProps}
        onChange={handleChange}
      />
    </TableRowCell>
  );
}

export default TableSelectCell;
