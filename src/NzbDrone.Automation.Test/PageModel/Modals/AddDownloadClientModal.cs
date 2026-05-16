using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// AddDownloadClient picker modal (frontend/src/Settings/DownloadClients/DownloadClients/AddDownloadClientModalContent.tsx).
/// Owned by Phase 20 Plan 20-05. Opened by clicking the empty add card on
/// /settings/downloadclients; lists schema implementations from GET /api/v5/downloadclient/schema
/// (InProcess is the D-06 canonical pick — only manga-aware client; qBittorrent /
/// SABnzbd / NZBGet carry-overs from upstream Sonarr have no manga-specific code
/// paths and are explicitly NOT covered per Phase 20 D-06).
/// </summary>
public class AddDownloadClientModal : PageBase
{
    public AddDownloadClientModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot      => Page.GetByTestId("add-downloadclient-modal");

    // Slug derived by AddDownloadClientItem.tsx using the same suffix-strip pattern
    // as Plan 20-04's AddIndexerItem.tsx: implementation.replace(/Indexer$/i, '')
    // .replace(/DownloadClient$/i, '').replace(/Notification$/i, '').toLowerCase().
    // For "InProcessImageDownloadClient" this yields "inprocessimage" (DownloadClient
    // suffix stripped, "Image" preserved). Plan 20-05 deviation #1 (Rule 3): plan-spec
    // used "inprocess" but the shared slug pattern produces "inprocessimage" — using
    // the canonical pattern unchanged so Plans 20-05/06 inherit the same shape.
    public ILocator InProcessCard  => Page.GetByTestId("add-downloadclient-inprocessimage");

    public ILocator SchemaCard(string slug)
        => Page.GetByTestId($"add-downloadclient-{slug}");
}
