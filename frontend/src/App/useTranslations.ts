// Sonarr divergence: Issue #39 — set translations synchronously in render so
// children that mount in the SAME render where `data` first becomes available
// see a populated dictionary. Upstream Sonarr's `useEffect` variant defers
// `setTranslations` to the post-commit phase, which is one tick too late for
// the catch-all `<Route path="*" component={NotFound} />` rendered on a fresh
// pageload of an unregistered URL. NotFound calls `translate('DefaultNotFoundMessage')`
// in its render body via a default-arg destructure; if that destructure runs
// before `setTranslations` commits, the in-module dict is still `{}` and the
// `translations[key] || key` fallback returns the raw key string. The component
// then never re-renders (no Redux/router subscription on its own), so the raw
// key sticks. Sidebar/header items happen to work because they re-render on
// route/state changes after the effect commits.
//
// Calling `setTranslations` synchronously during render is safe here: the
// underlying state is a module-scope mutable object and `translate()` is a
// best-effort lookup, not a React hook subscription. We guard with a `useMemo`
// keyed on `data` so the assignment runs at most once per fetch, not on every
// render of any component that calls `useTranslations`.
import { useMemo } from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { setTranslations } from 'Utilities/String/translate';

interface TranslationsResponse {
  strings: Record<string, string>;
}

export function useTranslations() {
  const { data, ...result } = useApiQuery<TranslationsResponse>({
    path: '/localization',
    queryOptions: {
      staleTime: Infinity,
      gcTime: Infinity,
    },
  });

  useMemo(() => {
    if (data) {
      setTranslations(data.strings);
    }
  }, [data]);

  return {
    ...result,
    data,
  };
}
