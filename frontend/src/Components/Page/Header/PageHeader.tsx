import React, { useCallback, useEffect, useState } from 'react';
import { toggleIsSidebarVisible } from 'App/appStore';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import useKeyboardShortcuts from 'Helpers/Hooks/useKeyboardShortcuts';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import KeyboardShortcutsModal from './KeyboardShortcutsModal';
import PageHeaderActionsMenu from './PageHeaderActionsMenu';
// Sonarr divergence: Phase 15 Plan 15-12 — SeriesSearchInput deleted in Plan 15-12
// (TV-only); manga search bar will land in v1.1+ as MangaSearchInput. Header rendering preserved without the search input until then.
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
          Sonarr divergence (minimal): retinted Sonarr's logo.svg in place — cyan
          accent (#0CF) → manga pink (#f06292). Same geometry, same path, same
          single-logo-per-theme pattern Sonarr ships; only the alt text changed
          ("Sonarr Logo" → "Mangarr"). No new SVG files added; no webpack-config
          divergence required.
        */}
        <Link className={styles.logoLink} to="/">
          <img
            className={styles.logo}
            src={`${window.Mangarr.urlBase}/Content/Images/logo.svg`}
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

      <div className={styles.right}>
        {/* Sonarr divergence: Phase 15 close-out F-A — Donate href preserved as upstream
            acknowledgment. Mangarr is a fork of Sonarr; this link routes user contributions
            to the upstream project. Decision documented in DIVERGENCE.md Phase 15 close. */}
        <IconButton
          className={styles.donate}
          name={icons.HEART}
          aria-label={translate('DonateToSonarr')}
          to="https://sonarr.tv/donate.html"
          size={14}
          title={translate('DonateToSonarr')}
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
