import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { useManageSettings, useSettings } from 'Settings/useSettings';

export interface IndexerSettingsModel {
  minimumAge: number;
  retention: number;
  maximumSize: number;
  rssSyncInterval: number;
  // Phase 33.2 D-05: app-wide Cloudflare solver endpoint URL. STRICTLY typed — selectSettings
  // keys per-field off this interface, so settings.cloudflareSolverUrl would not compile without it.
  cloudflareSolverUrl: string;
}

export interface CloudflareSolverTestResult {
  isValid: boolean;
  message: string;
}

const PATH = '/settings/indexer';

export const useIndexerSettings = () => {
  return useSettings<IndexerSettingsModel>(PATH);
};

export const useManageIndexerSettings = () => {
  return useManageSettings<IndexerSettingsModel>(PATH);
};

// Phase 33.2 D-05: Test Connection — POST settings/indexer/test probe-solves the configured
// sidecar. Returns a typed { isValid, message } result (never the cf_clearance cookie).
export const useTestCloudflareSolver = () => {
  return useApiMutation<CloudflareSolverTestResult, void>({
    path: `${PATH}/test`,
    method: 'POST',
  });
};
