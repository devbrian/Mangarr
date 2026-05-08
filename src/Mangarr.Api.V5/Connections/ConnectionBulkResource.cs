using NzbDrone.Core.Notifications;
using Mangarr.Api.V5.Provider;

namespace Mangarr.Api.V5.Connections;

public class ConnectionBulkResource : ProviderBulkResource<ConnectionBulkResource>
{
}

public class ConnectionBulkResourceMapper : ProviderBulkResourceMapper<ConnectionBulkResource, NotificationDefinition>
{
}
