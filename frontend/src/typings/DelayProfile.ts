import ModelBase from 'App/ModelBase';

interface DelayProfile extends ModelBase {
  name: string;
  enableUsenet: boolean;
  enableTorrent: boolean;
  preferredProtocol: string;
  usenetDelay: number;
  torrentDelay: number;
  httpDelay: number;
  bypassIfHighestQuality: boolean;
  bypassIfAboveCustomFormatScore: boolean;
  minimumCustomFormatScore: number;
  order: number;
  tags: number[];
}

export default DelayProfile;
