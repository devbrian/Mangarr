// Sonarr divergence: Phase 15 Plan 15-12 — STUB.
import React from 'react';
type Props = Record<string, unknown> & { status?: string };
export default function SeriesStatus(props: Props) {
  return <span>{(props.status as string) || ''}</span>;
}
export function getSeriesStatusDetails(_status?: string) {
  return { title: '', message: '', icon: 'rss' };
}
