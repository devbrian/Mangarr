// Sonarr divergence: Phase 21 close-out (2026-05-17) — Mangarr does NOT publish
// crash reports anywhere. The inherited Sonarr code initialized @sentry/browser
// against `sentry.sonarr.tv` (the upstream Sentry tenant), which is unreachable
// from a Mangarr deployment AND not legally ours to send data to anyway. Surfaced
// in PR #196 local browser smoke as a CORS error in the console:
//   `Access to fetch at 'https://sentry.sonarr.tv/api/12/envelope/?sentry_key=...'
//    has been blocked by CORS policy`
//
// Until v1.x introduces a Mangarr-owned crash-report endpoint (or a deliberate
// "no crash reporting, ever" product decision is recorded in PROJECT.md), this
// middleware is a no-op. The `@sentry/browser` import is a side-effect-free
// module load — it does NOT initiate any network call by itself; only sentry.init()
// would start the transport. By skipping the init AND returning undefined we
// guarantee zero crash-report traffic from the SPA.
//
// Keep the file (and the export) so the Redux store factory's
// `applyMiddleware(..., createSentryMiddleware())` continues to compile; Redux
// gracefully handles an `undefined` middleware. See bootstrap.tsx / createAppStore.js.

export default function createSentryMiddleware() {
  return;
}
