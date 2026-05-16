using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Modals;

/// <summary>
/// AddIndexer picker modal (frontend/src/Settings/Indexers/Indexers/AddIndexerModalContent.tsx).
/// Owned by Phase 20 Plan 20-04. Opened by clicking the empty add card on
/// /settings/indexers; lists schema implementations from GET /api/v5/indexer/schema
/// (Comix is disabled in TestKit OneTimeSetUp per Pitfall 10 so it does not
/// appear in the live picker enumeration that warms PuppeteerSharp).
/// </summary>
public class AddIndexerModal : PageBase
{
    public AddIndexerModal(IPage page)
        : base(page)
    {
    }

    public ILocator ModalRoot    => Page.GetByTestId("add-indexer-modal");
    public ILocator MangaDexCard => Page.GetByTestId("add-indexer-mangadex");

    public ILocator SchemaCard(string slug)
        => Page.GetByTestId($"add-indexer-{slug}");
}
