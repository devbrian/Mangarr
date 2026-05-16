// Sonarr divergence: Phase 15 Plan 15-12 — STUB page restored to satisfy the
// AppRoutes /settings/importlists route after the original ImportLists subtree
// was deleted (Plan 15-07). Manga import-list UI is v1.1+ work; this stub
// displays a 'not yet available' Alert so the navigation tree is intact and
// `yarn build` is green for the Wave 3.7 UI smoke test.
//
// Phase 8 cleanup: replace with the real manga import-list page once the
// backend manga import-list discriminator-extension lands (Phase 12-98 v1.1+).
import React from 'react';
import Alert from 'Components/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

function ImportListSettings() {
  return (
    <PageContent title={translate('ImportLists')}>
      <PageContentBody>
        {/* Phase 20 Plan 20-02 — route-load testid (D-18 selector strategy).
            v1.1+ placeholder per Phase 20 D-06 — no concrete providers ship pre-v1. */}
        <div data-testid="settings-importlists-page">
          <Alert kind={kinds.INFO}>
            {`${translate(
              'ImportLists'
            )} — not yet available for manga (v1.1+).`}
          </Alert>
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default ImportListSettings;
