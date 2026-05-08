// Sonarr divergence: Phase 15 Plan 15-12 — STUB.
import { kinds } from 'Helpers/Props';
export default function getProgressBarKind(
  status: string,
  monitored: boolean,
  progress: number,
  isDownloading: boolean
) {
  if (isDownloading) return kinds.PURPLE;
  if (progress === 100) {
    return status === 'ended' || status === 'completed' || status === 'cancelled'
      ? kinds.SUCCESS
      : kinds.PRIMARY;
  }
  if (monitored) return kinds.DANGER;
  return kinds.WARNING;
}
