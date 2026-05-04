import { QueryClientProvider } from '@tanstack/react-query';
import { ConnectedRouter, ConnectedRouterProps } from 'connected-react-router';
import React from 'react';
import DocumentTitle from 'react-document-title';
import { Provider } from 'react-redux';
import { Store } from 'redux';
import Page from 'Components/Page/Page';
import ApplyTheme from './ApplyTheme';
import AppRoutes from './AppRoutes';
import { queryClient } from './queryClient';

interface AppProps {
  store: Store;
  history: ConnectedRouterProps['history'];
}

// Sonarr divergence: per Phase 7 Plan 07-11 + UI-01 — browser tab title default — see DIVERGENCE.md.
// `window.Sonarr.instanceName` is a runtime config value (General Settings → Instance Name);
// falls back to "Mangarr" when unset so a fresh install displays "Mangarr" not blank.
// Phase 8 cleanup: rename `window.Sonarr` -> `window.Mangarr` alongside the system rename.
const APP_DEFAULT_TITLE = 'Mangarr';

function App({ store, history }: AppProps) {
  return (
    <DocumentTitle title={window.Sonarr.instanceName || APP_DEFAULT_TITLE}>
      <QueryClientProvider client={queryClient}>
        <Provider store={store}>
          <ConnectedRouter history={history}>
            <ApplyTheme />
            <Page>
              <AppRoutes />
            </Page>
          </ConnectedRouter>
        </Provider>
      </QueryClientProvider>
    </DocumentTitle>
  );
}

export default App;
