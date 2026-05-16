import React, { useCallback } from 'react';
import { useDispatch } from 'react-redux';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import Menu from 'Components/Menu/Menu';
import MenuContent from 'Components/Menu/MenuContent';
import { sizes } from 'Helpers/Props';
import { selectCustomFormatSpecificationSchema } from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import AddSpecificationPresetMenuItem from './AddSpecificationPresetMenuItem';
import styles from './AddSpecificationItem.css';

interface AddSpecificationItemProps {
  implementation: string;
  implementationName: string;
  infoLink: string;
  presets?: { name: string }[];
  onSpecificationSelect: () => void;
}

function AddSpecificationItem({
  implementation,
  implementationName,
  infoLink,
  presets,
  onSpecificationSelect,
}: AddSpecificationItemProps) {
  const dispatch = useDispatch();
  const hasPresets = !!presets && !!presets.length;

  const handleSpecificationSelect = useCallback(() => {
    dispatch(
      selectCustomFormatSpecificationSchema({
        implementation,
        implementationName,
      })
    );

    onSpecificationSelect();
  }, [implementation, implementationName, dispatch, onSpecificationSelect]);

  // debug-30 fix-forward (CodeRabbit #11) — mirror the
  // settings-customformat-card-{slug} precedent so Playwright fixtures can
  // target the underlay button by testid instead of via DOM traversal.
  // `settings-*` prefix is on the allowed list (D-18 selector strategy).
  const testIdSlug = implementationName.toLowerCase().replace(/\s+/g, '-');

  return (
    <div className={styles.specification}>
      <Link
        className={styles.underlay}
        data-testid={`settings-specification-card-${testIdSlug}`}
        onPress={handleSpecificationSelect}
      />

      <div className={styles.overlay}>
        <div className={styles.name}>{implementationName}</div>

        <div className={styles.actions}>
          {hasPresets ? (
            <span>
              <Button size={sizes.SMALL} onPress={handleSpecificationSelect}>
                {translate('Custom')}
              </Button>

              <Menu className={styles.presetsMenu}>
                <Button className={styles.presetsMenuButton} size={sizes.SMALL}>
                  {translate('Presets')}
                </Button>

                <MenuContent>
                  {presets.map((preset) => (
                    <AddSpecificationPresetMenuItem
                      key={preset.name}
                      name={preset.name}
                      implementation={implementation}
                      implementationName={implementationName}
                      onPress={handleSpecificationSelect}
                    />
                  ))}
                </MenuContent>
              </Menu>
            </span>
          ) : null}

          <Button to={infoLink} size={sizes.SMALL}>
            {translate('MoreInfo')}
          </Button>
        </div>
      </div>
    </div>
  );
}

export default AddSpecificationItem;
