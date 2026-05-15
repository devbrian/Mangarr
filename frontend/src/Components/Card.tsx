import React from 'react';
import Link, { LinkProps } from 'Components/Link/Link';
import styles from './Card.css';

interface CardProps extends Pick<LinkProps, 'onPress'> {
  // TODO: Consider using different properties for classname depending if it's overlaying content or not
  className?: string;
  overlayClassName?: string;
  overlayContent?: boolean;
  children: React.ReactNode;
  // Phase 18 / gh152-uat fix-forward: `data-testid` is propagated to the
  // underlay <Link> (the actual interactive <button>) when `overlayContent`
  // is true, and to the wrapping <Link> in the non-overlay branch. This
  // guarantees `Page.GetByTestId(...)` resolves to a clickable element —
  // clicking the inner text div on an overlay card otherwise triggers the
  // "Card-underlay intercepts pointer events" Playwright failure mode
  // (see .planning/debug/live-indexer-card-click.md).
  'data-testid'?: string;
}

function Card(props: CardProps) {
  const {
    className = styles.card,
    overlayClassName = styles.overlay,
    overlayContent = false,
    children,
    onPress,
    'data-testid': dataTestId,
  } = props;

  if (overlayContent) {
    return (
      <div className={className}>
        <Link
          className={styles.underlay}
          data-testid={dataTestId}
          onPress={onPress}
        />

        <div className={overlayClassName}>{children}</div>
      </div>
    );
  }

  return (
    <Link className={className} data-testid={dataTestId} onPress={onPress}>
      {children}
    </Link>
  );
}

export default Card;
