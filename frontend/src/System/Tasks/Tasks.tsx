import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import pageContentBodyStyles from 'Components/Page/PageContentBody.css';
import translate from 'Utilities/String/translate';
import QueuedTasks from './Queued/QueuedTasks';
import ScheduledTasks from './Scheduled/ScheduledTasks';

function Tasks() {
  return (
    <PageContent title={translate('Tasks')}>
      <div
        className={pageContentBodyStyles.contentBodyWrapper}
        data-testid="system-tasks-page"
      >
        <PageContentBody>
          <ScheduledTasks />
          <QueuedTasks />
        </PageContentBody>
      </div>
    </PageContent>
  );
}

export default Tasks;
