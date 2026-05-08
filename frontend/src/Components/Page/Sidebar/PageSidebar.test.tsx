import { links } from './PageSidebar';

describe('PageSidebar', () => {
  it('PageSidebar_Settings_children_excludes_Quality_nav_link', () => {
    const settings = links.find((link) => link.to === '/settings');
    expect(settings).toBeDefined();
    const qualityChild = settings!.children!.find(
      (c) => c.to === '/settings/quality'
    );
    expect(qualityChild).toBeUndefined();
  });
});
