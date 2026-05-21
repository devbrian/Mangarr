using FluentValidation;
using Mangarr.Api.V5.Provider;
using Mangarr.Http;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.ImportLists;

// D-14: authored from src/Mangarr.Api.V5/Indexers/IndexerController.cs:11 17-line
// ProviderControllerBase template; main controller NOT preserved in reference slice
// (.planning/reference/sonarr-vertical-slices/import-lists/v5-controller/ contains
// only the Exclusion family — ExclusionController + ExclusionResource +
// ExclusionBulkResource + ExclusionExistsValidator).
//
// Inherits the full 10-endpoint CRUD surface from ProviderControllerBase (verified
// in 26-RESEARCH.md §Q5):
//   GET    /api/v5/importlist          (list)
//   GET    /api/v5/importlist/{id}     (by-id)
//   POST   /api/v5/importlist          (create)
//   PUT    /api/v5/importlist/{id}     (update)
//   DELETE /api/v5/importlist/{id}     (delete)
//   GET    /api/v5/importlist/schema   (provider templates — returns [] until Phase 27)
//   POST   /api/v5/importlist/test     (test definition)
//   POST   /api/v5/importlist/testall  (test all enabled)
//   PUT    /api/v5/importlist/bulk     (bulk update)
//   DELETE /api/v5/importlist/bulk     (bulk delete)
//
// Phase 27.1 D-08-amendment + RESEARCH OQ 5: bulk-edit can now mutate
// RootFolderPath + TranslationProfileId via /bulk inherited endpoint — guard at
// controller layer to prevent silent corruption (T-27.1-01-03 + T-27.1-01-04
// STRIDE Tamper mitigations). Mirrors Sonarr v3 ImportListController:14-22
// pattern (which carries the upstream RootFolderExistsValidator +
// QualityProfileExistsValidator rules). Field-shape adapted to Mangarr:
//   * QualityProfile → TranslationProfile (Phase 5 D-04 model)
//   * Validators are reused-as-is from src/NzbDrone.Core/Validation/
//     (DryIoc resolves them through assembly-scan auto-registration).
//
// Provider-base validator rules (Name not empty / unique, Implementation +
// ConfigContract not empty, Fields not null) come from ProviderControllerBase
// ctor at lines 44-49.
[V5ApiController]
public class ImportListController : ProviderControllerBase<ImportListResource, ImportListBulkResource, IMangaImportList, ImportListDefinition>
{
    public static readonly ImportListResourceMapper ResourceMapper = new();
    public static readonly ImportListBulkResourceMapper BulkResourceMapper = new();

    public ImportListController(IBroadcastSignalRMessage signalRBroadcaster,
                                IImportListFactory importListFactory,
                                RootFolderExistsValidator rootFolderExistsValidator,
                                TranslationProfileExistsValidator translationProfileExistsValidator)
        : base(signalRBroadcaster, importListFactory, "importlist", ResourceMapper, BulkResourceMapper)
    {
        SharedValidator.RuleFor(x => x.RootFolderPath).Cascade(CascadeMode.Stop)
            .IsValidPath()
            .SetValidator(rootFolderExistsValidator)
            .When(x => x.RootFolderPath.IsNotNullOrWhiteSpace());

        // ImportListResource.TranslationProfileId is non-nullable int. The
        // TranslationProfileExistsValidator.IsValid contract short-circuits to true
        // when (int)PropertyValue == 0, preserving the "no change" / fallback case
        // for partial PUT /bulk payloads where the client omits the field.
        SharedValidator.RuleFor(x => x.TranslationProfileId).SetValidator(translationProfileExistsValidator);
    }
}
