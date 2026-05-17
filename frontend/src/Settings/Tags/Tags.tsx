import React, { useEffect } from 'react';
import { useDispatch } from 'react-redux';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { kinds } from 'Helpers/Props';
import { useIndexers } from 'Settings/Indexers/useIndexers';
import { useConnections } from 'Settings/Notifications/useConnections';
import { useReleaseProfiles } from 'Settings/Profiles/Release/useReleaseProfiles';
import { fetchDownloadClients } from 'Store/Actions/settingsActions';
import useTagDetails from 'Tags/useTagDetails';
import useTags, { useSortedTagList } from 'Tags/useTags';
import translate from 'Utilities/String/translate';
import Tag from './Tag';
import styles from './Tags.css';

function Tags() {
  const dispatch = useDispatch();

  const { isFetching, isFetched, error } = useTags();
  const items = useSortedTagList();
  const {
    isFetching: isDetailsFetching,
    isFetched: isDetailsFetched,
    error: detailsError,
  } = useTagDetails();

  useReleaseProfiles();
  useConnections();
  useIndexers();

  // Phase 22 Plan 22-05 F-3 surgical fix (Option A) — DelayProfile and
  // ImportList Redux dispatches were removed here (their V5 controllers
  // do not exist yet; Phase 23 ships DelayProfile, Phase 26 ships ImportList).
  // The redundant queryClient.invalidateQueries(['releaseprofile']) line was
  // also removed — useReleaseProfiles() above already subscribes to that query.
  // The DownloadClient dispatch is retained because the V5 controller ships
  // today and the in-use details modal (TagDetailsModalContent) reads
  // state.settings.downloadClients.items via useSelector to render its
  // matched-items list. The modal's three useSelector reads will be ported
  // to React Query hooks alongside the new V5 controllers in Phase 23 / 26.
  // See DIVERGENCE.md "Phase 22 — Tags.tsx Redux-dispatch removal" and
  // .planning/phases/22-tags-repair-v1-1-inserted-2026-05-17/22-05-GREP-VERIFY.md
  // for the verify-before-delete attestation.
  useEffect(() => {
    dispatch(fetchDownloadClients());
  }, [dispatch]);

  if (!items.length) {
    return (
      <Alert kind={kinds.INFO}>{translate('NoTagsHaveBeenAddedYet')}</Alert>
    );
  }

  return (
    <FieldSet legend={translate('Tags')}>
      <PageSectionContent
        errorMessage={translate('TagsLoadError')}
        error={error || detailsError}
        isFetching={isFetching || isDetailsFetching}
        isPopulated={isFetched && isDetailsFetched}
      >
        <div className={styles.tags}>
          {items.map((item) => {
            return <Tag key={item.id} {...item} />;
          })}
        </div>
      </PageSectionContent>
    </FieldSet>
  );
}

export default Tags;
