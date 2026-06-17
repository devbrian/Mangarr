import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import pageContentBodyStyles from 'Components/Page/PageContentBody.css';
import translate from 'Utilities/String/translate';
import About from './About/About';
import DiskSpace from './DiskSpace/DiskSpace';
import Health from './Health/Health';
import MoreInfo from './MoreInfo/MoreInfo';

function Status() {
  return (
    <PageContent title={translate('Status')}>
      <div
        className={pageContentBodyStyles.contentBodyWrapper}
        data-testid="system-status-page"
      >
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
