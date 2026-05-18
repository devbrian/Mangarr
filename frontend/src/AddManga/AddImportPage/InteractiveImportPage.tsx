// Sonarr divergence: NEW per Phase 25 Plan 25-03 Task 2 (D-02) — see
// DIVERGENCE.md.
//
// Full inline page hosting the shared InteractiveImportContent shipped in
// Plan 25-03 Task 1. This is the Sonarr-canonical alternative to the
// Wanted/Missing modal surface (D-03): same content, no <Modal> chrome,
// rendered inside the project's PageContent + PageContentBody shell.
//
// Cancel → history.push('/') per CONTEXT.md specifics A6 + RESEARCH §Q3.
// Routed at /add/import via AppRoutes.tsx (also touched in this task).
//
// Pitfall 16 cross-plan boundary: this commit does NOT touch
// frontend/src/Wanted/Missing/Missing.tsx (LOCK guard drop reserved for
// Plan 25-05 LAST CODE commit per Pitfall 16 sequencing).
import React, { useCallback } from 'react';
import { useHistory } from 'react-router-dom';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import InteractiveImportContent from 'InteractiveImport/Interactive/InteractiveImportContent';
import translate from 'Utilities/String/translate';

function InteractiveImportPage() {
  const history = useHistory();

  const handleCancel = useCallback(() => {
    // Page exit → MangaIndex per CONTEXT.md specifics A6 + RESEARCH §Q3.
    history.push('/');
  }, [history]);

  return (
    <PageContent title={translate('ManualImport')}>
      <PageContentBody>
        <div data-testid="add-import-page">
          <InteractiveImportContent
            showImportMode={true}
            showFilterExistingFiles={true}
            onCancel={handleCancel}
          />
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default InteractiveImportPage;
