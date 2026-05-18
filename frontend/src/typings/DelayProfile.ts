import ModelBase from 'App/ModelBase';

interface DelayProfile extends ModelBase {
  name: string;
  preferredProtocol: string;
  httpDelay: number;
  bypassIfHighestQuality: boolean;
  bypassIfAboveCustomFormatScore: boolean;
  minimumCustomFormatScore: number;
  order: number;
  tags: number[];
}

export default DelayProfile;
