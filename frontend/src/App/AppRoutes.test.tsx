// GH #174 regression test — UrlBase redirect SPA bug.
//
// Pins the route-declaration-order contract in AppRoutes.tsx: when
// `window.Mangarr.urlBase` is non-empty, the RedirectWithUrlBase <Route> must
// appear in the <Switch> child list BEFORE the unconditional MangaIndex <Route>
// so React Router v5's first-match-wins resolves to the redirect on "/" rather
// than rendering MangaIndex under the urlBase path.
//
// Project testing convention note (see frontend/src/Components/Page/Sidebar/
// PageSidebar.test.tsx for the precedent): this project does not currently
// ship a Jest / Vitest runtime. `.test.ts(x)` files are excluded from the
// webpack bundle (frontend/build/webpack.config.js exclude rule
// `\.test\.tsx?$`) and the TS check (frontend/tsconfig.json `exclude` list).
// The test body is authored against a standard `describe` / `it` / `expect`
// jsdom shape so it lights up unchanged once a Jest infra plan ships.
//
// In the meantime the live regression marker is the Phase 20 Playwright fixture
// `src/NzbDrone.Automation.Test/Tests/Routes/UrlBaseRedirectFixture.cs` which
// asserts `window.location.pathname` actually changes — that fixture is the
// authoritative production guard.
import React from 'react';
import { Route } from 'react-router-dom';
import AppRoutes from './AppRoutes';

interface WindowWithMangarr {
  Mangarr: { urlBase: string };
}

function withUrlBase<T>(urlBase: string, fn: () => T): T {
  const w = window as unknown as WindowWithMangarr;
  const prior = w.Mangarr;
  w.Mangarr = { ...(prior ?? { urlBase: '' }), urlBase };
  try {
    return fn();
  } finally {
    w.Mangarr = prior;
  }
}

function getSwitchChildren(tree: React.ReactElement): React.ReactNode[] {
  const children = (tree.props as { children?: React.ReactNode }).children;
  return React.Children.toArray(children);
}

function isRouteElement(
  node: React.ReactNode
): node is React.ReactElement<{
  path?: string;
  exact?: boolean;
  component?: unknown;
  render?: unknown;
}> {
  return React.isValidElement(node) && node.type === Route;
}

describe('AppRoutes', () => {
  it('AppRoutes_redirect_route_declared_before_MangaIndex_when_urlBase_set', () => {
    withUrlBase('/mangarr', () => {
      const tree = AppRoutes();
      const children = getSwitchChildren(tree);

      // Locate the redirect Route (path "/" with a render prop) and the
      // MangaIndex Route (path "/" with a component prop) by their identifying
      // shape — order-by-index matters here.
      let redirectIndex = -1;
      let mangaIndexIndex = -1;

      children.forEach((child, i) => {
        if (!isRouteElement(child)) {
          return;
        }
        const { path, exact, render, component } = child.props;
        if (path !== '/' || exact !== true) {
          return;
        }
        if (typeof render === 'function' && redirectIndex === -1) {
          redirectIndex = i;
        } else if (
          typeof component === 'function' &&
          mangaIndexIndex === -1
        ) {
          mangaIndexIndex = i;
        }
      });

      expect(redirectIndex).toBeGreaterThan(-1);
      expect(mangaIndexIndex).toBeGreaterThan(-1);
      expect(redirectIndex).toBeLessThan(mangaIndexIndex);
    });
  });

  it('AppRoutes_redirect_route_omitted_when_urlBase_empty', () => {
    withUrlBase('', () => {
      const tree = AppRoutes();
      const children = getSwitchChildren(tree);

      // With urlBase falsy the conditional `{window.Mangarr.urlBase && (...)}`
      // short-circuits to `false`; React.Children.toArray strips boolean
      // children, so the only Route matching path "/" exact=true should be
      // the unconditional MangaIndex Route (component prop, no render prop).
      const rootRoutes = children.filter(
        (child): child is React.ReactElement<{
          path?: string;
          exact?: boolean;
          component?: unknown;
          render?: unknown;
        }> => isRouteElement(child) && child.props.path === '/' && child.props.exact === true
      );

      expect(rootRoutes).toHaveLength(1);
      expect(typeof rootRoutes[0].props.render).toBe('undefined');
      expect(typeof rootRoutes[0].props.component).toBe('function');
    });
  });
});
