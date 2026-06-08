import React from 'react';
import Link from 'Components/Link/Link';
import { useMetadataSourcesData } from 'Settings/MetadataSource/useMetadataSources';
import translate from 'Utilities/String/translate';
import styles from './MetadataAttribution.css';

export default function MetadataAttribution() {
  // Resolve the actual primary metadata source name rather than hard-coding one.
  // Phase 41 made MangaBaka the default primary (MangaDex demoted to a fallback),
  // and the user can change the primary at any time — the attribution must follow
  // the configured primary, not a fixed string.
  const sources = useMetadataSourcesData();
  const primary = sources.find((source) => source.isPrimary);
  const provider = primary?.name ?? translate('MetadataSource');

  return (
    <div className={styles.container}>
      <Link className={styles.attribution} to="/settings/metadatasource">
        {translate('MetadataProvidedBy', { provider })}
      </Link>
    </div>
  );
}
