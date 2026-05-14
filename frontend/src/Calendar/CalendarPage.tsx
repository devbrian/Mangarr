// Sonarr divergence: Phase 15 Plan 15-12 — STUB Calendar page.
// Phase 18 Plan 18-06: Calendar is a v2 placeholder per UI-06; the
// `calendar-page` + `calendar-grid` testids let CalendarFixture verify
// the page doesn't white-screen without requiring full functionality.
import React from 'react';
import Alert from 'Components/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

function CalendarPage() {
  return (
    <PageContent title={translate('Calendar')}>
      <PageContentBody>
        <div data-testid="calendar-page">
          <div data-testid="calendar-grid">
            <Alert kind={kinds.INFO}>
              {`${translate('Calendar')} will arrive in v1.1+ for manga.`}
            </Alert>
          </div>
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default CalendarPage;
