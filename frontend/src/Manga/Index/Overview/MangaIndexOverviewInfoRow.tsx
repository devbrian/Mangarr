import React from 'react';
import Icon, { IconName } from 'Components/Icon';
import styles from './MangaIndexOverviewInfoRow.css';

interface MangaIndexOverviewInfoRowProps {
  title?: string;
  iconName: IconName;
  label: string | null;
}

function MangaIndexOverviewInfoRow(props: MangaIndexOverviewInfoRowProps) {
  const { title, iconName, label } = props;

  return (
    <div className={styles.infoRow} title={title}>
      <Icon className={styles.icon} name={iconName} size={14} />

      {label}
    </div>
  );
}

export default MangaIndexOverviewInfoRow;
