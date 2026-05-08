// Sonarr divergence: Phase 15 Plan 15-12 — STUB.
import React from 'react';
type Props = Record<string, unknown>;
export default function EpisodeTitleLink(props: Props) {
  const text = (props.episodeTitle as string) || (props.title as string) || '';
  return <span>{text}</span>;
}
