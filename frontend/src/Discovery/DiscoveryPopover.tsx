// Phase 42 — click-to-open popover for the Discovery cards.
//
// The shared Components/Tooltip/Popover enables click only on mobile (hover on
// desktop). The Discovery cards need an explicit CLICK affordance on the cover
// Tags pill + the card title (hovering every title to reveal a description would
// be noisy across a 20-card grid). This mirrors Tooltip's floating-ui setup but
// with click (not hover) as the trigger, and reuses the same Tooltip/Popover CSS
// so it reads identically to the rest of the app.
import {
  arrow,
  autoUpdate,
  flip,
  FloatingArrow,
  FloatingPortal,
  offset,
  Placement,
  shift,
  useClick,
  useDismiss,
  useFloating,
  useInteractions,
} from '@floating-ui/react';
import classNames from 'classnames';
import React, { useRef, useState } from 'react';
import popoverStyles from 'Components/Tooltip/Popover.css';
import tooltipStyles from 'Components/Tooltip/Tooltip.css';
import { useThemeColor } from 'Helpers/Hooks/useTheme';

interface DiscoveryPopoverProps {
  className?: string;
  anchor: React.ReactNode;
  title: string;
  body: React.ReactNode;
  position?: Placement;
}

function DiscoveryPopover({
  className,
  anchor,
  title,
  body,
  position = 'top',
}: DiscoveryPopoverProps) {
  const arrowColor = useThemeColor('popoverArrowBorderColor');
  const [isOpen, setIsOpen] = useState(false);
  const arrowRef = useRef(null);

  const { refs, context, floatingStyles } = useFloating({
    middleware: [
      arrow({ element: arrowRef }),
      flip(),
      offset({ mainAxis: 10 }),
      shift({ padding: 8 }),
    ],
    open: isOpen,
    placement: position,
    whileElementsMounted: autoUpdate,
    onOpenChange: setIsOpen,
  });

  const click = useClick(context);
  const dismiss = useDismiss(context);
  const { getReferenceProps, getFloatingProps } = useInteractions([
    click,
    dismiss,
  ]);

  return (
    <>
      <span
        ref={refs.setReference}
        {...getReferenceProps()}
        className={className}
      >
        {anchor}
      </span>

      {isOpen ? (
        <FloatingPortal id="portal-root">
          <div
            ref={refs.setFloating}
            className={tooltipStyles.tooltipContainer}
            style={floatingStyles}
            {...getFloatingProps()}
          >
            <FloatingArrow ref={arrowRef} context={context} fill={arrowColor} />

            <div
              className={classNames(
                tooltipStyles.tooltip,
                tooltipStyles.default
              )}
            >
              <div className={popoverStyles.tooltipBody}>
                <div className={popoverStyles.title}>{title}</div>
                <div className={popoverStyles.body}>{body}</div>
              </div>
            </div>
          </div>
        </FloatingPortal>
      ) : null}
    </>
  );
}

export default DiscoveryPopover;
