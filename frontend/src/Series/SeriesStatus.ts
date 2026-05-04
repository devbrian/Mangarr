// Phase 7 Plan 07-04: status type widened to `string` so the manga sibling can
// pass MangaStatus values ('ongoing' / 'hiatus' / 'completed' / 'cancelled' /
// 'unknown') in addition to the original SeriesStatus values. The runtime
// branches each guard against a literal — manga statuses fall through to the
// 'continuing' default which is the cheapest semantically-correct mapping
// (Phase 8 collapses this helper into a manga-aware sibling).
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

export function getSeriesStatusDetails(status: string) {
  let statusDetails = {
    icon: icons.SERIES_CONTINUING,
    title: translate('Continuing'),
    message: translate('ContinuingSeriesDescription'),
  };

  if (status === 'deleted' || status === 'cancelled') {
    statusDetails = {
      icon: icons.SERIES_DELETED,
      title: translate('Deleted'),
      message: translate('DeletedSeriesDescription'),
    };
  } else if (status === 'ended' || status === 'completed') {
    statusDetails = {
      icon: icons.SERIES_ENDED,
      title: translate('Ended'),
      message: translate('EndedSeriesDescription'),
    };
  } else if (status === 'upcoming') {
    statusDetails = {
      icon: icons.SERIES_CONTINUING,
      title: translate('Upcoming'),
      message: translate('UpcomingSeriesDescription'),
    };
  }

  return statusDetails;
}
