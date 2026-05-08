// Sonarr divergence: Phase 15 Plan 15-12 — STUB Calendar page.
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
        <Alert kind={kinds.INFO}>
          {translate('Calendar') + ' will arrive in v1.1+ for manga.'}
        </Alert>
      </PageContentBody>
    </PageContent>
  );
}

export default CalendarPage;
