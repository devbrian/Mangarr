import React from 'react';
import DocumentTitle from 'react-document-title';
import ErrorBoundary from 'Components/Error/ErrorBoundary';
import PageContentError from './PageContentError';
import styles from './PageContent.css';

interface PageContentProps {
  className?: string;
  title?: string;
  children: React.ReactNode;
}

// Sonarr divergence: per Phase 7 Plan 07-11 + UI-01 — browser tab title default — see DIVERGENCE.md.
// Falls back to "Mangarr" when `window.Mangarr.instanceName` is empty/undefined so per-page
// titles still read "<Page> - Mangarr" not "<Page> - " on a fresh install.
const PAGE_DEFAULT_TITLE = 'Mangarr';

function PageContent({
  className = styles.content,
  title,
  children,
}: PageContentProps) {
  const instance = window.Mangarr.instanceName || PAGE_DEFAULT_TITLE;

  return (
    <ErrorBoundary errorComponent={PageContentError}>
      <DocumentTitle title={title ? `${title} - ${instance}` : instance}>
        <div className={className}>{children}</div>
      </DocumentTitle>
    </ErrorBoundary>
  );
}

export default PageContent;
