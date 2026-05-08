// Sonarr divergence: Phase 15 Plan 15-12 — STUB Quality + QualityModel types.
import ModelBase from 'App/ModelBase';
interface Quality extends ModelBase {
  name: string;
  source?: string;
  resolution?: number;
  // QualityProfileQualityItem inlined fields (Sonarr-shape carry-over for legacy
  // consumers that destructure off Quality directly).
  minSize?: number | null;
  maxSize?: number | null;
  preferredSize?: number | null;
}
export interface Revision {
  version: number;
  real: number;
  isRepack: boolean;
}
export interface QualityModel {
  quality: Quality;
  revision: Revision;
}
export default Quality;
