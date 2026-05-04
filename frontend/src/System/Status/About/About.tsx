import React, { useEffect } from 'react';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import useSystemStatus from '../useSystemStatus';
import StartTime from './StartTime';
import styles from './About.css';

function About() {
  const { data, refetch } = useSystemStatus();

  const {
    version,
    packageVersion,
    packageAuthor,
    isNetCore,
    isContainerized,
    runtimeVersion,
    databaseVersion,
    databaseType,
    migrationVersion,
    appData,
    startupPath,
    mode,
    startTime,
  } = data;

  useEffect(() => {
    refetch();
  }, [refetch]);

  return (
    <FieldSet legend={translate('About')}>
      <DescriptionList className={styles.descriptionList}>
        {/*
          Sonarr divergence: per Phase 7 Plan 07-11 + UI-01 + UI-SPEC §Rebrand Chrome Contract
          ("Sidebar About text Sonarr v... -> Mangarr v...") — see DIVERGENCE.md.
          The product-name row reads "Mangarr" (i18n key landed in Plan 07-11 en.json).
          Sibling MoreInfo.tsx still links to sonarr.tv/wiki/forums per D-06 — those are
          attribution links to the upstream fork's resources, not user-visible product copy.
        */}
        <DescriptionListItem
          title={translate('Application')}
          data={translate('Mangarr')}
        />
        <DescriptionListItem title={translate('Version')} data={version} />

        {packageVersion && (
          <DescriptionListItem
            title={translate('PackageVersion')}
            data={
              packageAuthor ? (
                <InlineMarkdown
                  data={translate('PackageVersionInfo', {
                    packageVersion,
                    packageAuthor,
                  })}
                />
              ) : (
                packageVersion
              )
            }
          />
        )}

        {isNetCore ? (
          <DescriptionListItem
            title={translate('DotNetVersion')}
            data={`Yes (${runtimeVersion})`}
          />
        ) : null}

        {isContainerized ? (
          <DescriptionListItem title={translate('Docker')} data="Yes" />
        ) : null}

        <DescriptionListItem
          title={translate('Database')}
          data={`${titleCase(databaseType)} ${databaseVersion}`}
        />

        <DescriptionListItem
          title={translate('DatabaseMigration')}
          data={migrationVersion}
        />

        <DescriptionListItem
          title={translate('AppDataDirectory')}
          data={appData}
        />

        <DescriptionListItem
          title={translate('StartupDirectory')}
          data={startupPath}
        />

        <DescriptionListItem title={translate('Mode')} data={titleCase(mode)} />

        <DescriptionListItem
          title={translate('Uptime')}
          data={<StartTime startTime={startTime} />}
        />
      </DescriptionList>
    </FieldSet>
  );
}

export default About;
