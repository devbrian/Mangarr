using Mangarr.Http.ClientSchema;
using NzbDrone.Core.AutoTagging.Specifications;

namespace Mangarr.Api.V5.AutoTagging;

// Phase 24 Plan 24-04 — V5 AutoTaggingSpecificationResource mapper (a.k.a.
// SchemaMapper). Port from Sonarr v5-develop's AutoTaggingSpecificationSchemaMapper
// at the pinned SHA dfb157382b20a2d4eb5f5828a6c1e276c0d6b160.
//
// ToResource — builds the schema row from an IAutoTaggingSpecification instance
// via SchemaBuilder.ToSchema reflection on [FieldDefinition] attributes. This is
// the SAME reflection pipeline used by every other provider schema endpoint
// (Indexers, DownloadClients, ImportLists, Notifications) — no AutoTagging-specific
// reflection code introduced; just a one-call delegation per Sonarr-canonical
// pattern.
//
// ToModel — looks up the matching IAutoTaggingSpecification impl by class name from
// the injected DryIoc-resolved catalog (the 11-spec list from 24-03), then uses
// SchemaBuilder.ReadFromSchema to instantiate the concrete spec from the wire
// Fields[]. Copies Name/Negate/Required onto the result. Throws
// ArgumentException on unknown Implementation strings — controller surfaces as
// validation error.
public static class AutoTaggingSpecificationResourceMapper
{
    public static AutoTaggingSpecificationResource ToResource(this IAutoTaggingSpecification model)
    {
        return new AutoTaggingSpecificationResource
        {
            Name = model.Name,
            Implementation = model.GetType().Name,
            ImplementationName = model.ImplementationName,
            Negate = model.Negate,
            Required = model.Required,
            Fields = SchemaBuilder.ToSchema(model)
        };
    }

    public static IAutoTaggingSpecification ToModel(this AutoTaggingSpecificationResource resource,
                                                    List<IAutoTaggingSpecification> specifications)
    {
        var matchingSpec = specifications.SingleOrDefault(x => x.GetType().Name == resource.Implementation);

        if (matchingSpec is null)
        {
            throw new ArgumentException(
                $"{resource.Implementation} is not a valid specification implementation");
        }

        var type = matchingSpec.GetType();

        // Sonarr V5 convention: pass null as the model arg so SchemaBuilder.ReadFromSchema
        // creates a fresh instance via Activator.CreateInstance. The Privacy short-circuit
        // in SchemaBuilder requires a model arg only when a Privacy-tagged field re-uses an
        // existing PRIVATE_VALUE marker; AutoTagging specs have no Privacy fields so null
        // is safe (verified by inspection of all 11 specs in 24-03).
        var spec = (IAutoTaggingSpecification)SchemaBuilder.ReadFromSchema(resource.Fields, type, null);
        spec.Name = resource.Name;
        spec.Negate = resource.Negate;
        spec.Required = resource.Required;
        return spec;
    }
}
