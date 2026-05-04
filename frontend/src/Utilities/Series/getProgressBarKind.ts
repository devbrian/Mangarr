// Phase 7 Plan 07-04: status type widened to `string` so the manga sibling can
// pass MangaStatus values ('completed' / 'cancelled' / 'ongoing' / 'hiatus' /
// 'unknown') in addition to the original SeriesStatus values
// ('ended' / 'continuing' / 'upcoming' / 'deleted'). Behaviour preserved for
// TV — only the 'ended' literal influenced the colour pick anyway.
import { kinds } from 'Helpers/Props';

function getProgressBarKind(
  status: string,
  monitored: boolean,
  progress: number,
  isDownloading: boolean
) {
  if (isDownloading) {
    return kinds.PURPLE;
  }

  if (progress === 100) {
    return status === 'ended' || status === 'completed' || status === 'cancelled'
      ? kinds.SUCCESS
      : kinds.PRIMARY;
  }

  if (monitored) {
    return kinds.DANGER;
  }

  return kinds.WARNING;
}

export default getProgressBarKind;
