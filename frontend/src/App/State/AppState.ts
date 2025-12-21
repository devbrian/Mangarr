import BlocklistAppState from './BlocklistAppState';
import CaptchaAppState from './CaptchaAppState';
import ImportSeriesAppState from './ImportSeriesAppState';
import InteractiveImportAppState from './InteractiveImportAppState';
import OAuthAppState from './OAuthAppState';
import ProviderOptionsAppState from './ProviderOptionsAppState';
import SettingsAppState from './SettingsAppState';

interface AppState {
  blocklist: BlocklistAppState;
  captcha: CaptchaAppState;
  importSeries: ImportSeriesAppState;
  interactiveImport: InteractiveImportAppState;
  oAuth: OAuthAppState;
  providerOptions: ProviderOptionsAppState;
  settings: SettingsAppState;
}

export default AppState;
