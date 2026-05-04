// Sonarr divergence: per Phase 7 D-05 — Profiles tab repurposed to TranslationProfiles editor — see DIVERGENCE.md.
// Role-match analog: this same file pre-Phase-7 (rendered <QualityProfiles /> + <DelayProfiles /> + <ReleaseProfiles />).
//
// UI-07: Settings → Profiles renders the manga TranslationProfile editor wiring /api/v5/translationprofile (Phase 5).
//
// Manga sibling preserves: PageContent + PageContentBody + DndProvider scaffold (single drag-drop context constraint
// preserved for Translations editor's language-rank drag-list).
//
// Manga sibling diverges from Sonarr Profiles:
//   * Renders <TranslationProfiles /> in the body instead of <QualityProfiles /> + <DelayProfiles /> + <ReleaseProfiles />
//   * Page title swapped from translate('Profiles') to translate('TranslationProfiles')
//
// Phase 8 cleanup: when Quality/Delay/Release sub-trees delete, this page renders only the manga TranslationProfiles editor.
// Quality/Delay/Release sub-tree imports retained as commented-out lines per Pitfall 8 grep-fidelity (Phase 8 deletes them).

import { HTML5toTouch } from 'rdndmb-html5-to-touch';
import React from 'react';
import { DndProvider } from 'react-dnd-multi-backend';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import SettingsToolbar from 'Settings/SettingsToolbar';
import translate from 'Utilities/String/translate';
import TranslationProfiles from './Translations/TranslationProfiles';
// import DelayProfiles from './Delay/DelayProfiles';      // Phase 8 — kept import comment for grep fidelity (Pitfall 8)
// import QualityProfiles from './Quality/QualityProfiles'; // Phase 8 — kept import comment for grep fidelity (Pitfall 8)
// import ReleaseProfiles from './Release/ReleaseProfiles'; // Phase 8 — kept import comment for grep fidelity (Pitfall 8)

// Only a single DragDrop Context can exist so it's done here to allow editing
// translation profile language-rank drag-lists.

function Profiles() {
  return (
    <PageContent title={translate('TranslationProfiles')}>
      <SettingsToolbar showSave={false} />

      <PageContentBody>
        <DndProvider options={HTML5toTouch}>
          <TranslationProfiles />
        </DndProvider>
      </PageContentBody>
    </PageContent>
  );
}

export default Profiles;
