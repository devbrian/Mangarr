// Sonarr divergence: per Phase 7 D-05 — Profiles tab repurposed to TranslationProfiles editor — see DIVERGENCE.md.
// Phase 12 sub-wave C (Plan 12-07) — F-01 closure: Delay + Release editors RE-EXPOSED after Phase 7 D-05 hid them.
// Phase 7 Lock #2 RETAINED — QualityProfiles import stays commented (Quality deletion lives in Phase 15).
// Role-match analog: this same file pre-Phase-7 (rendered <QualityProfiles /> + <DelayProfiles /> + <ReleaseProfiles />).
//
// UI-07: Settings → Profiles renders the manga TranslationProfile editor (primary) + Delay editor + Release editor.
//
// Manga sibling preserves: PageContent + PageContentBody + DndProvider scaffold (single drag-drop context constraint
// preserved for Translations editor's language-rank drag-list AND Release editor's tag-reorder drag-list).
//
// Manga sibling diverges from Sonarr Profiles:
//   * <QualityProfiles /> NOT rendered (Phase 7 D-05 + Lock #2 — Quality replaced by TranslationProfiles for v1)
//   * Page title swapped from translate('Profiles') to translate('TranslationProfiles')
//
// Phase 8 cleanup: when Quality sub-tree deletes (Phase 15 D-12), the QualityProfiles import comment also deletes.
// QualityProfiles import comment retained per Pitfall 8 grep-fidelity (Phase 15 deletes both the file and this comment).

import { HTML5toTouch } from 'rdndmb-html5-to-touch';
import React from 'react';
import { DndProvider } from 'react-dnd-multi-backend';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import SettingsToolbar from 'Settings/SettingsToolbar';
import translate from 'Utilities/String/translate';
import TranslationProfiles from './Translations/TranslationProfiles';
import DelayProfiles from './Delay/DelayProfiles';        // Phase 12 sub-wave C (Plan 12-07) — F-01 closure: re-exposed after Phase 7 D-05
// import QualityProfiles from './Quality/QualityProfiles'; // Phase 7 Lock #2 retained — Quality deletion lives in Phase 15 (Pitfall 8 grep-fidelity)
import ReleaseProfiles from './Release/ReleaseProfiles';  // Phase 12 sub-wave C (Plan 12-07) — Release/ closure: re-exposed alongside Delay/

// Only a single DragDrop Context can exist so it's done here to allow editing
// translation profile language-rank drag-lists.

function Profiles() {
  return (
    <PageContent title={translate('TranslationProfiles')}>
      <SettingsToolbar showSave={false} />

      <PageContentBody>
        <DndProvider options={HTML5toTouch}>
          <TranslationProfiles />
          <DelayProfiles />
          <ReleaseProfiles />
        </DndProvider>
      </PageContentBody>
    </PageContent>
  );
}

export default Profiles;
