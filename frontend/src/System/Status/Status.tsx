import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';
import About from './About/About';
import DiskSpace from './DiskSpace/DiskSpace';
import Health from './Health/Health';
import MoreInfo from './MoreInfo/MoreInfo';

function Status() {
  return (
    <PageContent title={translate('Status')}>
      <div data-testid="system-status-page">
        <PageContentBody>
          <Health />
          <DiskSpace />
          <About />
          <MoreInfo />
        </PageContentBody>
      </div>
    </PageContent>
  );
}

export default Status;
