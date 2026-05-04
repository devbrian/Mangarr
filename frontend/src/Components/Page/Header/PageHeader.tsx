import React, { useCallback, useEffect, useState } from 'react';
import { toggleIsSidebarVisible } from 'App/appStore';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import useKeyboardShortcuts from 'Helpers/Hooks/useKeyboardShortcuts';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import KeyboardShortcutsModal from './KeyboardShortcutsModal';
import PageHeaderActionsMenu from './PageHeaderActionsMenu';
import SeriesSearchInput from './SeriesSearchInput';
import styles from './PageHeader.css';

function PageHeader() {
  const [isKeyboardShortcutsModalOpen, setIsKeyboardShortcutsModalOpen] =
    useState(false);

  const { bindShortcut, unbindShortcut } = useKeyboardShortcuts();

  const handleSidebarToggle = useCallback(() => {
    toggleIsSidebarVisible();
  }, []);

  const handleOpenKeyboardShortcutsModal = useCallback(() => {
    setIsKeyboardShortcutsModalOpen(true);
  }, []);

  const handleKeyboardShortcutsModalClose = useCallback(() => {
    setIsKeyboardShortcutsModalOpen(false);
  }, []);

  useEffect(() => {
    bindShortcut(
      'openKeyboardShortcutsModal',
      handleOpenKeyboardShortcutsModal
    );

    return () => {
      unbindShortcut('openKeyboardShortcutsModal');
    };
  }, [handleOpenKeyboardShortcutsModal, bindShortcut, unbindShortcut]);

  return (
    <div className={styles.header}>
      <div className={styles.logoContainer}>
        {/*
          Sonarr divergence: per Phase 7 Plan 07-11 + UI-01 + UI-SPEC §Rebrand Chrome Contract — see DIVERGENCE.md.
          Logo asset: Content/Images/logo.svg -> Content/Images/Logos/mangarr-light.svg
          alt text: "Sonarr Logo" -> "Mangarr". The wordmark is part of the SVG asset itself
          (see frontend/src/Content/Images/Logos/mangarr-light.svg) so a separate <span>
          wordmark element is not required here. Existing TV pages keep their Sonarr-rendered
          bits per D-06; this is the global chrome only.
        */}
        <Link className={styles.logoLink} to="/">
          <img
            className={styles.logo}
            src={`${window.Sonarr.urlBase}/Content/Images/Logos/mangarr-light.svg`}
            alt="Mangarr"
          />
        </Link>
      </div>

      <div className={styles.sidebarToggleContainer}>
        <IconButton
          id="sidebar-toggle-button"
          name={icons.NAVBAR_COLLAPSE}
          aria-label={translate('Menu')}
          onPress={handleSidebarToggle}
        />
      </div>

      <SeriesSearchInput />

      <div className={styles.right}>
        <IconButton
          className={styles.donate}
          name={icons.HEART}
          aria-label={translate('Donate')}
          to="https://sonarr.tv/donate.html"
          size={14}
          title={translate('Donate')}
        />

        <PageHeaderActionsMenu
          onKeyboardShortcutsPress={handleOpenKeyboardShortcutsModal}
        />
      </div>

      <KeyboardShortcutsModal
        isOpen={isKeyboardShortcutsModalOpen}
        onModalClose={handleKeyboardShortcutsModalClose}
      />
    </div>
  );
}

export default PageHeader;
