import classNames from 'classnames';
import React, { Children, useCallback } from 'react';
import Icon, { IconName } from 'Components/Icon';
import Link from 'Components/Link/Link';
import styles from './PageSidebarItem.css';

export interface PageSidebarItemProps {
  iconName?: IconName;
  title: string | (() => string);
  to: string;
  isActive?: boolean;
  isActiveParent?: boolean;
  isParentItem?: boolean;
  isChildItem?: boolean;
  statusComponent?: React.ElementType;
  children?: React.ReactNode;
  onPress?: () => void;
  // Phase 18 Plan-03 — additive data-testid plumb-through to the rendered <Link>.
  // Used by PageSidebar.tsx links config to expose nav-{section} testids on the
  // 6 top-nav items per inventory/data-testid-spec.md. <Link> already propagates
  // data-testid via ...otherProps (verified Phase 18 Plan-01 Wrapper Ledger),
  // so the testid lands on the underlying DOM <a> / RouterLink element.
  dataTestId?: string;
}

function PageSidebarItem({
  iconName,
  title,
  to,
  isActive,
  isActiveParent,
  isChildItem = false,
  isParentItem = false,
  statusComponent: StatusComponent,
  children,
  onPress,
  dataTestId,
}: PageSidebarItemProps) {
  const handlePress = useCallback(() => {
    if (isChildItem || !isParentItem) {
      onPress?.();
    }
  }, [isChildItem, isParentItem, onPress]);

  return (
    <div
      className={classNames(styles.item, isActiveParent && styles.isActiveItem)}
    >
      <Link
        className={classNames(
          isChildItem ? styles.childLink : styles.link,
          isActiveParent && styles.isActiveParentLink,
          isActive && styles.isActiveLink
        )}
        to={to}
        aria-current={isActive ? 'page' : undefined}
        data-testid={dataTestId}
        onPress={handlePress}
      >
        {!!iconName && (
          <span className={styles.iconContainer}>
            <Icon name={iconName} aria-hidden={true} />
          </span>
        )}

        {typeof title === 'function' ? title() : title}

        {!!StatusComponent && (
          <span className={styles.status}>
            <StatusComponent />
          </span>
        )}
      </Link>

      {children
        ? Children.map(children, (child) => {
            if (!React.isValidElement(child)) {
              return child;
            }

            const childProps = { isChildItem: true };

            return React.cloneElement(child, childProps);
          })
        : null}
    </div>
  );
}

export default PageSidebarItem;
