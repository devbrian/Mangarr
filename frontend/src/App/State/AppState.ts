import BlocklistAppState from './BlocklistAppState';
import CaptchaAppState from './CaptchaAppState';
import ImportSeriesAppState from './ImportSeriesAppState';
import OAuthAppState from './OAuthAppState';
import ProviderOptionsAppState from './ProviderOptionsAppState';
import SettingsAppState from './SettingsAppState';

interface AppState {
  blocklist: BlocklistAppState;
  captcha: CaptchaAppState;
  importSeries: ImportSeriesAppState;
  oAuth: OAuthAppState;
  providerOptions: ProviderOptionsAppState;
  settings: SettingsAppState;
}

export default AppState;
