import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import ReactDOM from 'react-dom';
import { useLocation } from 'react-router';
import QueueStatus from 'Activity/Queue/Status/QueueStatus';
import {
  setIsSidebarVisible,
  useAppDimension,
  useAppValue,
} from 'App/appStore';
import { IconName } from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import OverlayScroller from 'Components/Scroller/OverlayScroller';
import Scroller from 'Components/Scroller/Scroller';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { icons } from 'Helpers/Props';
import dimensions from 'Styles/Variables/dimensions';
import HealthStatus from 'System/Status/Health/HealthStatus';
import translate from 'Utilities/String/translate';
import Messages from './Messages/Messages';
import PageSidebarItem from './PageSidebarItem';
import styles from './PageSidebar.css';

const HEADER_HEIGHT = parseInt(dimensions.headerHeight);
const SIDEBAR_WIDTH = parseInt(dimensions.sidebarWidth);

interface SidebarItem {
  iconName?: IconName;
  title: string | (() => string);
  to: string;
  alias?: string;
  isActive?: boolean;
  isActiveParent?: boolean;
  isParentItem?: boolean;
  isChildItem?: boolean;
  statusComponent?: React.ElementType;
  // Phase 18 Plan-03 — nav-{section} data-testid carried on each top-level
  // entry per inventory/data-testid-spec.md. Threaded through PageSidebarItem
  // -> Link -> rendered DOM <a>. AutomationTest PageBase uses GetByTestId
  // ("nav-manga") etc. for navigation locators (PageBase.cs lines 16-21).
  dataTestId?: string;
  children?: {
    title: string | (() => string);
    to: string;
    statusComponent?: React.ElementType;
  }[];
}

// Phase 15 Plan 15-07 (Wave 3) cutover — TV nav entries flipped to manga-rooted siblings:
//   * Top-level Series -> Manga rebrand (root path '/' now renders MangaIndex per AppRoutes
//     cutover; alias updated from '/series' to '/manga').
//   * Activity entries flipped from /activity/{queue,history,blocklist} -> /manga/activity/*.
//   * Wanted entries flipped from /wanted/{missing,cutoffunmet} -> /manga/wanted/*.
//   * Quality false-spread guard DELETED entirely (paired atomically with Settings.tsx
//     {false && (...)} JSX block delete in Plan 15-07 Task 6).
//
// Phase 25.1 (PR #205, merged 2026-05-19) reopened the manga folder-import flow:
// the `/add/import` + `/add/import/:rootFolderId` routes ship the library-import
// UI in Sonarr-canonical shape, reversing the Phase 15 Plan 15-07 D-12-09
// Out-of-Scope decision. PR #206 brought the import grid to visual parity.
// The `LibraryImport` sub-nav child was missed during Phase 25.1's route
// registration and is restored here per issue #208.
export const links: SidebarItem[] = [
  {
    iconName: icons.SERIES_CONTINUING,
    title: () => translate('Manga'),
    to: '/',
    alias: '/manga',
    dataTestId: 'nav-manga',
    children: [
      {
        title: () => translate('AddNew'),
        to: '/add/manga',
      },
      {
        title: () => translate('LibraryImport'),
        to: '/add/import',
      },
    ],
  },

  {
    iconName: icons.SEARCH,
    title: () => translate('Discovery'),
    to: '/discovery',
    dataTestId: 'nav-discovery',
  },

  {
    iconName: icons.CALENDAR,
    title: () => translate('Calendar'),
    to: '/calendar',
    dataTestId: 'nav-calendar',
  },

  {
    iconName: icons.ACTIVITY,
    title: () => translate('Activity'),
    to: '/manga/activity/queue',
    dataTestId: 'nav-activity',
    children: [
      {
        title: () => translate('Queue'),
        to: '/manga/activity/queue',
        statusComponent: QueueStatus,
      },
      {
        title: () => translate('History'),
        to: '/manga/activity/history',
      },
      {
        title: () => translate('Blocklist'),
        to: '/manga/activity/blocklist',
      },
    ],
  },

  {
    iconName: icons.WARNING,
    title: () => translate('Wanted'),
    to: '/manga/wanted/missing',
    dataTestId: 'nav-wanted',
    children: [
      {
        title: () => translate('Missing'),
        to: '/manga/wanted/missing',
      },
      {
        title: () => translate('CutoffUnmet'),
        to: '/manga/wanted/cutoffunmet',
      },
    ],
  },

  {
    iconName: icons.SETTINGS,
    title: () => translate('Settings'),
    to: '/settings',
    dataTestId: 'nav-settings',
    children: [
      {
        title: () => translate('MediaManagement'),
        to: '/settings/mediamanagement',
      },
      {
        title: () => translate('Profiles'),
        to: '/settings/profiles',
      },
      // Sonarr divergence: Phase 15 Plan 15-07 D-12 — Quality nav DELETED entirely
      // (paired with Settings.tsx false-guard block delete in same wave). The
      // Phase 7 D-05 `false ? [{...}] : []` spread guard previously here is gone.
      // quick-260608-gmm: the Custom Format Profiles row was present in Settings.tsx
      // (the /settings landing page) but missing here, so the sidebar jumped Profiles ->
      // Custom Formats and the page was only reachable via the landing page / direct URL.
      // Added to match Settings.tsx ordering (Profiles -> Custom Format Profiles -> Custom Formats).
      {
        title: () => translate('CustomFormatProfiles'),
        to: '/settings/customformatprofiles',
      },
      {
        title: () => translate('CustomFormats'),
        to: '/settings/customformats',
      },
      {
        title: () => translate('Indexers'),
        to: '/settings/indexers',
      },
      {
        title: () => translate('DownloadClients'),
        to: '/settings/downloadclients',
      },
      {
        title: () => translate('ImportLists'),
        to: '/settings/importlists',
      },
      {
        title: () => translate('Connect'),
        to: '/settings/connect',
      },
      {
        title: () => translate('Metadata'),
        to: '/settings/metadata',
      },
      {
        title: () => translate('MetadataSource'),
        to: '/settings/metadatasource',
      },
      {
        title: () => translate('Tags'),
        to: '/settings/tags',
      },
      {
        title: () => translate('General'),
        to: '/settings/general',
      },
      {
        title: () => translate('Ui'),
        to: '/settings/ui',
      },
    ],
  },

  {
    iconName: icons.SYSTEM,
    title: () => translate('System'),
    to: '/system/status',
    dataTestId: 'nav-system',
    children: [
      {
        title: () => translate('Status'),
        to: '/system/status',
        statusComponent: HealthStatus,
      },
      {
        title: () => translate('Tasks'),
        to: '/system/tasks',
      },
      {
        title: () => translate('Backup'),
        to: '/system/backup',
      },
      {
        title: () => translate('Updates'),
        to: '/system/updates',
      },
      {
        title: () => translate('Events'),
        to: '/system/events',
      },
      {
        title: () => translate('LogFiles'),
        to: '/system/logs/files',
      },
    ],
  },
];

function hasActiveChildLink(link: SidebarItem, pathname: string) {
  const children = link.children;

  if (!children || !children.length) {
    return false;
  }

  return children.some((child) => {
    return child.to === pathname;
  });
}

function PageSidebar() {
  const isSidebarVisible = useAppValue('isSidebarVisible');
  const isSmallScreen = useAppDimension('isSmallScreen');
  const location = useLocation();
  const sidebarRef = useRef(null);
  const touchStartX = useRef<number | null>(null);
  const touchStartY = useRef<number | null>();
  const wasSidebarVisible = usePrevious(isSidebarVisible);

  const [sidebarTransform, setSidebarTransform] = useState<{
    transition: string;
    transform: number;
  }>({
    transition: 'none',
    transform: isSidebarVisible ? 0 : SIDEBAR_WIDTH * -1,
  });

  const urlBase = window.Mangarr.urlBase;
  const pathname = urlBase
    ? location.pathname.substr(urlBase.length) || '/'
    : location.pathname;

  // Mangarr fix (nav-subheaders-missing, 2026-05-11): two-pass specificity-first
  // selection. The single-pass `find` of upstream Sonarr was safe only while
  // every top-level nav prefix was disjoint (Sonarr: /series, /activity, /wanted).
  // Phase 15-07 Wave 3 rebrand flipped the Manga alias to `/manga` and the
  // Activity / Wanted nav `to` paths to `/manga/activity/*` and `/manga/wanted/*`,
  // making `/manga` a prefix of every other manga-rooted top-level target. The
  // single-pass find then short-circuited on the Manga link's alias for every
  // /manga/* URL, leaving Activity and Wanted unable to become the active parent
  // and silently suppressing their child sub-headers.
  //
  // Fix: prefer the most-specific match (exact link.to OR a child whose `to`
  // matches the pathname) before falling back to the alias / prefix match. This
  // restores the Sonarr-canonical specificity order without altering the
  // single-pass behavior when the prefixes are disjoint (the second pass is
  // unreached in that case).
  const activeParent = useMemo(() => {
    // Pass 1: most-specific — exact link.to match, OR a child whose `to`
    // matches the pathname (longest-prefix among children wins).
    const specificMatch = links.find((link) => {
      if (link.to && link.to === pathname) {
        return true;
      }

      const children = link.children;

      if (children) {
        const matchingChild = children.find((childLink) => {
          return pathname.startsWith(childLink.to);
        });

        if (matchingChild) {
          return true;
        }
      }

      return false;
    });

    if (specificMatch) {
      return specificMatch.to;
    }

    // Pass 2: fall back to prefix / alias match.
    const prefixMatch = links.find((link) => {
      if (link.to !== '/' && pathname.startsWith(link.to)) {
        return true;
      }

      if (link.alias && pathname.startsWith(link.alias)) {
        return true;
      }

      return false;
    });

    return prefixMatch?.to ?? links[0].to;
  }, [pathname]);

  const handleWindowClick = useCallback(
    (event: MouseEvent) => {
      const sidebar = ReactDOM.findDOMNode(sidebarRef.current);
      const toggleButton = document.getElementById('sidebar-toggle-button');
      const target = event.target;

      if (!sidebar) {
        return;
      }

      if (
        target instanceof Node &&
        !sidebar.contains(target) &&
        !toggleButton?.contains(target) &&
        isSidebarVisible
      ) {
        event.preventDefault();
        event.stopPropagation();
        setIsSidebarVisible({ isSidebarVisible: false });
      }
    },
    [isSidebarVisible]
  );

  const handleItemPress = useCallback(() => {
    setIsSidebarVisible({ isSidebarVisible: false });
  }, []);

  const handleTouchStart = useCallback(
    (event: TouchEvent) => {
      const touches = event.touches;
      const x = touches[0].pageX;
      const y = touches[0].pageY;

      if (touches.length !== 1) {
        return;
      }

      if (isSidebarVisible && (x > 210 || x < 180)) {
        return;
      } else if (!isSidebarVisible && x > 40) {
        return;
      }

      touchStartX.current = x;
      touchStartY.current = y;
    },
    [isSidebarVisible]
  );

  const handleTouchMove = useCallback((event: TouchEvent) => {
    const touches = event.touches;
    const currentTouchX = touches[0].pageX;
    // const currentTouchY = touches[0].pageY;
    // const isSidebarVisible = this.props.isSidebarVisible;

    if (!touchStartX.current) {
      return;
    }

    if (Math.abs(touchStartX.current - currentTouchX) < 40) {
      return;
    }

    const transform = Math.min(currentTouchX - SIDEBAR_WIDTH, 0);

    setSidebarTransform({
      transition: 'none',
      transform,
    });
  }, []);

  const handleTouchEnd = useCallback(
    (event: TouchEvent) => {
      const touches = event.changedTouches;
      const currentTouch = touches[0].pageX;

      if (!touchStartX.current) {
        return;
      }

      if (currentTouch > touchStartX.current && currentTouch > 50) {
        setSidebarTransform({
          transition: 'none',
          transform: 0,
        });
      } else if (currentTouch < touchStartX.current && currentTouch < 80) {
        setSidebarTransform({
          transition: 'transform 50ms ease-in-out',
          transform: SIDEBAR_WIDTH * -1,
        });
      } else {
        setSidebarTransform({
          transition: 'none',
          transform: isSidebarVisible ? 0 : SIDEBAR_WIDTH * -1,
        });
      }

      touchStartX.current = null;
      touchStartY.current = null;
    },
    [isSidebarVisible]
  );

  const handleTouchCancel = useCallback(() => {
    touchStartX.current = null;
    touchStartY.current = null;
  }, []);

  const handleSidebarClosePress = useCallback(() => {
    setIsSidebarVisible({ isSidebarVisible: false });
  }, []);

  useEffect(() => {
    if (isSmallScreen) {
      window.addEventListener('click', handleWindowClick, { capture: true });
      window.addEventListener('touchstart', handleTouchStart);
      window.addEventListener('touchmove', handleTouchMove);
      window.addEventListener('touchend', handleTouchEnd);
      window.addEventListener('touchcancel', handleTouchCancel);
    }

    return () => {
      window.removeEventListener('click', handleWindowClick, { capture: true });
      window.removeEventListener('touchstart', handleTouchStart);
      window.removeEventListener('touchmove', handleTouchMove);
      window.removeEventListener('touchend', handleTouchEnd);
      window.removeEventListener('touchcancel', handleTouchCancel);
    };
  }, [
    isSmallScreen,
    handleWindowClick,
    handleTouchStart,
    handleTouchMove,
    handleTouchEnd,
    handleTouchCancel,
  ]);

  useEffect(() => {
    if (wasSidebarVisible !== isSidebarVisible) {
      setSidebarTransform({
        transition: 'none',
        transform: isSidebarVisible ? 0 : SIDEBAR_WIDTH * -1,
      });
    } else if (sidebarTransform.transform === 0 && !isSidebarVisible) {
      setIsSidebarVisible({ isSidebarVisible: true });
    } else if (
      sidebarTransform.transform === -SIDEBAR_WIDTH &&
      isSidebarVisible
    ) {
      setIsSidebarVisible({ isSidebarVisible: false });
    }
  }, [sidebarTransform, isSidebarVisible, wasSidebarVisible]);

  const containerStyle = useMemo(() => {
    if (!isSmallScreen) {
      return undefined;
    }

    return {
      transition: sidebarTransform.transition ?? 'none',
      transform: `translateX(${sidebarTransform.transform}px)`,
    };
  }, [isSmallScreen, sidebarTransform]);

  const ScrollerComponent = isSmallScreen ? Scroller : OverlayScroller;

  return (
    <nav
      ref={sidebarRef}
      className={styles.sidebarContainer}
      style={containerStyle}
      aria-label={translate('MainNavigation')}
    >
      {isSmallScreen ? (
        <div className={styles.sidebarHeader}>
          <div className={styles.logoContainer}>
            <Link className={styles.logoLink} to="/">
              <img
                className={styles.logo}
                src={`${window.Mangarr.urlBase}/Content/Images/logo.svg`}
                alt="Sonarr Logo"
              />
            </Link>
          </div>

          <IconButton
            className={styles.sidebarCloseButton}
            name={icons.CLOSE}
            aria-label={translate('Close')}
            size={20}
            onPress={handleSidebarClosePress}
          />
        </div>
      ) : null}

      <ScrollerComponent
        className={styles.sidebar}
        scrollDirection="vertical"
        style={{
          height: `${window.innerHeight - HEADER_HEIGHT}px`,
        }}
      >
        <div>
          {links.map((link) => {
            const childWithStatusComponent = link.children?.find((child) => {
              return !!child.statusComponent;
            });

            const childStatusComponent = childWithStatusComponent
              ? childWithStatusComponent.statusComponent
              : null;

            const isActiveParent = activeParent === link.to;
            const hasActiveChild = hasActiveChildLink(link, pathname);

            return (
              <PageSidebarItem
                key={link.to}
                iconName={link.iconName}
                title={link.title}
                to={link.to}
                dataTestId={link.dataTestId}
                statusComponent={
                  isActiveParent || !childStatusComponent
                    ? link.statusComponent
                    : childStatusComponent
                }
                isActive={pathname === link.to && !hasActiveChild}
                isActiveParent={isActiveParent}
                isParentItem={!!link.children}
                onPress={handleItemPress}
              >
                {link.children &&
                  link.to === activeParent &&
                  link.children.map((child) => {
                    return (
                      <PageSidebarItem
                        key={child.to}
                        title={child.title}
                        to={child.to}
                        isActive={pathname === child.to}
                        isParentItem={false}
                        isChildItem={true}
                        statusComponent={child.statusComponent}
                        onPress={handleItemPress}
                      />
                    );
                  })}
              </PageSidebarItem>
            );
          })}
        </div>

        <Messages />
      </ScrollerComponent>
    </nav>
  );
}

export default PageSidebar;
