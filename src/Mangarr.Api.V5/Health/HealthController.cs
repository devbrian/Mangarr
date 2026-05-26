using Mangarr.Http;
using Mangarr.Http.REST;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.Health;

[V5ApiController]
public class HealthController : RestControllerWithSignalR<HealthResource, HealthCheck>,
                            IHandle<HealthCheckCompleteEvent>
{
    private readonly IHealthCheckService _healthCheckService;

    public HealthController(IBroadcastSignalRMessage signalRBroadcaster, IHealthCheckService healthCheckService)
        : base(signalRBroadcaster)
    {
        _healthCheckService = healthCheckService;
    }

    [NonAction]
    public override Results<Ok<HealthResource>, NotFound> GetResourceByIdWithErrorHandler(int id)
    {
        return base.GetResourceByIdWithErrorHandler(id);
    }

    protected override HealthResource GetResourceById(int id)
    {
        // Intentional: Health is a collection-only resource (served via GetHealth below); there is no get-by-id
        // semantics for a health check. The abstract base requires this override, so it throws as an intentional
        // stub — the [NonAction] GetResourceByIdWithErrorHandler above means it is never routed. NOT a bug (CQ-05 / DOCS-07).
        throw new NotImplementedException();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<HealthResource>> GetHealth()
    {
        return TypedResults.Ok(_healthCheckService.Results().ToResource());
    }

    [NonAction]
    public void Handle(HealthCheckCompleteEvent message)
    {
        BroadcastResourceChange(ModelAction.Sync);
    }
}
