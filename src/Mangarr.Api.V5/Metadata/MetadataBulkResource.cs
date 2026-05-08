using NzbDrone.Core.Extras.Metadata;
using Mangarr.Api.V5.Provider;

namespace Mangarr.Api.V5.Metadata;

public class MetadataBulkResource : ProviderBulkResource<MetadataBulkResource>
{
}

public class MetadataBulkResourceMapper : ProviderBulkResourceMapper<MetadataBulkResource, MetadataDefinition>
{
}
