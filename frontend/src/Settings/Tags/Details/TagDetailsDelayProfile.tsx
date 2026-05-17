import React from 'react';
import translate from 'Utilities/String/translate';

interface TagDetailsDelayProfileProps {
  httpDelay: number;
}

function TagDetailsDelayProfile({ httpDelay }: TagDetailsDelayProfileProps) {
  return (
    <div>
      <div>{translate('HttpDelayTime', { httpDelay })}</div>
    </div>
  );
}

export default TagDetailsDelayProfile;
