// Sonarr divergence: Phase 21 close-out (2026-05-17) — Mangarr publishes no
// crash reports anywhere; the `@sentry/browser` import + `sentry.captureException`
// call were removed from this file. Companion change in
// Store/Middleware/createSentryMiddleware.js neuters the Redux Sentry
// middleware. Re-wire when a Mangarr-owned crash-report endpoint exists
// (v1.x consideration).
import React, { Component, ErrorInfo } from 'react';

interface ErrorBoundaryProps {
  children: React.ReactNode;
  errorComponent: React.ElementType;
  onModalClose?: () => void;
}

interface ErrorBoundaryState {
  error: Error | null;
  info: ErrorInfo | null;
}

// Class component until componentDidCatch is supported in functional components
class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);

    this.state = {
      error: null,
      info: null,
    };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    this.setState({
      error,
      info,
    });

    // Sonarr divergence: Phase 21 close-out — Mangarr publishes no crash
    // reports anywhere. The error is rendered via the ErrorComponent below
    // and logged to the browser console; no external transport.
    // sentry.captureException(error);
  }

  render() {
    const {
      children,
      errorComponent: ErrorComponent,
      onModalClose,
    } = this.props;
    const { error, info } = this.state;

    if (error) {
      return (
        <ErrorComponent error={error} info={info} onModalClose={onModalClose} />
      );
    }

    return children;
  }
}

export default ErrorBoundary;
