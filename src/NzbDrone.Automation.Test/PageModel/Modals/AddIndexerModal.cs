using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// AddIndexer picker modal (frontend/src/Settings/Indexers/Indexers/AddIndexerModalContent.tsx).
/// Owned by Phase 20 Plan 20-04. Opened by clicking the empty add card on
/// /settings/indexers; lists schema implementations from GET /api/v5/indexer/schema.
/// Phase 39 Plan 39-07: the sole <c>IIndexer</c> is the <c>GatewayIndexer</c> (the in-process
/// site-scraper indexers were retired in Plan 39-03), so the only schema card is the gateway
/// (testid <c>add-indexer-gateway</c> — <c>GatewayIndexer</c> strips the <c>Indexer</c> suffix
/// per AddIndexerItem.tsx).
/// </summary>
public class AddIndexerModal : PageBase
{
    public AddIndexerModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot    => Page.GetByTestId("add-indexer-modal");
    public ILocator GatewayCard  => Page.GetByTestId("add-indexer-gateway");

    public ILocator SchemaCard(string slug)
        => Page.GetByTestId($"add-indexer-{slug}");
}
