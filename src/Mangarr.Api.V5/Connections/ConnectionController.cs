using Mangarr.Api.V5.Provider;
using Mangarr.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Notifications;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.Connections;

[V5ApiController]
public class ConnectionController : ProviderControllerBase<ConnectionResource, ConnectionBulkResource, INotification, NotificationDefinition>
{
    public static readonly ConnectionResourceMapper ResourceMapper = new();
    public static readonly ConnectionBulkResourceMapper BulkResourceMapper = new();

    public ConnectionController(IBroadcastSignalRMessage signalRBroadcaster, NotificationFactory notificationFactory)
        : base(signalRBroadcaster, notificationFactory, "connection", ResourceMapper, BulkResourceMapper)
    {
    }

    [NonAction]
    public override Results<Ok<IEnumerable<ConnectionResource>>, BadRequest> UpdateProvider([FromBody] ConnectionBulkResource providerResource)
    {
        // Intentional: the [NonAction] attribute hides this inherited bulk REST verb, which does not apply to the
        // single-instance Connection provider surface. ASP.NET never routes a [NonAction] method, so this throw is
        // unreachable — it satisfies the abstract base override only. NOT a bug (CQ-05 / DOCS-07).
        throw new NotImplementedException();
    }

    [NonAction]
    public override NoContent DeleteProviders([FromBody] ConnectionBulkResource resource)
    {
        // Intentional: the [NonAction] attribute hides this inherited bulk REST verb, which does not apply to the
        // single-instance Connection provider surface. ASP.NET never routes a [NonAction] method, so this throw is
        // unreachable — it satisfies the abstract base override only. NOT a bug (CQ-05 / DOCS-07).
        throw new NotImplementedException();
    }
}
