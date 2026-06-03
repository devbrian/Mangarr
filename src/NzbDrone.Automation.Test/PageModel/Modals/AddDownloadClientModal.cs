using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// AddDownloadClient picker modal (frontend/src/Settings/DownloadClients/DownloadClients/AddDownloadClientModalContent.tsx).
/// Owned by Phase 20 Plan 20-05. Opened by clicking the empty add card on
/// /settings/downloadclients; lists schema implementations from GET /api/v5/downloadclient/schema.
/// Phase 39 Plan 39-07: GatewayDownloadClient is the sole manga download client (the in-process
/// image downloader was retired in Plan 39-02); qBittorrent / SABnzbd / NZBGet carry-overs from
/// upstream Sonarr have no manga-specific code paths and are explicitly NOT covered.
/// </summary>
public class AddDownloadClientModal : PageBase
{
    public AddDownloadClientModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot      => Page.GetByTestId("add-downloadclient-modal");

    // Slug derived by AddDownloadClientItem.tsx using the suffix-strip pattern:
    // implementation.replace(/Indexer$/i, '').replace(/DownloadClient$/i, '')
    // .replace(/Notification$/i, '').toLowerCase(). For "GatewayDownloadClient" this
    // strips the "DownloadClient" suffix to yield slug "gateway" → testid
    // "add-downloadclient-gateway" (Phase 39 Plan 39-07 — the sole manga download client
    // post-retirement of the in-process image downloader).
    public ILocator GatewayCard  => Page.GetByTestId("add-downloadclient-gateway");

    public ILocator SchemaCard(string slug)
        => Page.GetByTestId($"add-downloadclient-{slug}");
}
