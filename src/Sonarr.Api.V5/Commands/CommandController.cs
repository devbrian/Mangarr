using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Composition;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;
using Sonarr.Http.Validation;
using Debouncer = NzbDrone.Common.TPL.Debouncer;

namespace Sonarr.Api.V5.Commands;

// Phase 11 review WR-04: StartCommand below declares Results<Created<CommandResource>, NotFound>
// but CommandTypeResolver.Resolve can throw BadRequestException (e.g. when contractName is
// supplied but does not match any candidate FullName, or — post-WR-01/05 fix — when the
// multi-match tv-prefer rule does not select exactly one candidate). BadRequestException
// derives from ApiException and is converted to HTTP 400 by the global error pipeline
// (see Sonarr.Http.Exceptions.ApiException + middleware), NOT returned as a typed result.
//
// The 400 response body therefore follows the global error contract, not Created<CommandResource>
// or NotFound. OpenAPI generation that reads ONLY the typed-results signature will not see
// the 400 schema — clients should consult the global error contract for the BadRequest body
// shape. We deliberately do NOT widen the typed return to include BadRequest<TError> because
// that would imply the controller body itself returns a typed BadRequest, which it does not.
[V5ApiController]
public class CommandController : RestControllerWithSignalR<CommandResource, CommandModel>, IHandle<CommandUpdatedEvent>
{
    private readonly IManageCommandQueue _commandQueueManager;
    private readonly KnownTypes _knownTypes;
    private readonly Debouncer _debouncer;
    private readonly Dictionary<int, CommandResource> _pendingUpdates;

    private readonly CommandPriorityComparer _commandPriorityComparer = new();

    public CommandController(IManageCommandQueue commandQueueManager,
                         IBroadcastSignalRMessage signalRBroadcaster,
                         KnownTypes knownTypes)
        : base(signalRBroadcaster)
    {
        _commandQueueManager = commandQueueManager;
        _knownTypes = knownTypes;

        _debouncer = new Debouncer(SendUpdates, TimeSpan.FromSeconds(0.1));
        _pendingUpdates = new Dictionary<int, CommandResource>();

        PostValidator.RuleFor(c => c.Name).NotBlank();
    }

    protected override CommandResource GetResourceById(int id)
    {
        return _commandQueueManager.Get(id).ToResource();
    }

    [RestPostById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Created<CommandResource>, NotFound> StartCommand([FromBody] CommandResource commandResource)
    {
        var commandType = CommandTypeResolver.Resolve(_knownTypes, commandResource.Name, commandResource.ContractName);

        Request.Body.Seek(0, SeekOrigin.Begin);
        using (var reader = new StreamReader(Request.Body))
        {
            var body = reader.ReadToEnd();
            var command = STJson.Deserialize(body, commandType) as Command;

            if (command == null)
            {
                throw new BadRequestException("Invalid command body");
            }

            command.SuppressMessages = !command.SendUpdatesToClient;
            command.SendUpdatesToClient = true;
            command.ClientUserAgent = Request.Headers["UserAgent"];

            var trackedCommand = _commandQueueManager.Push(command, commandResource.Priority, CommandTrigger.Manual);

            return TypedCreated(trackedCommand.Id);
        }
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<CommandResource>> GetStartedCommands()
    {
        return TypedResults.Ok(_commandQueueManager.All()
            .OrderBy(c => c.Status, _commandPriorityComparer)
            .ThenByDescending(c => c.Priority)
            .ToResource());
    }

    [RestDeleteById]
    public NoContent CancelCommand(int id)
    {
        _commandQueueManager.Cancel(id);

        return TypedResults.NoContent();
    }

    [NonAction]
    public void Handle(CommandUpdatedEvent message)
    {
        if (message.Command.Body.SendUpdatesToClient)
        {
            lock (_pendingUpdates)
            {
                _pendingUpdates[message.Command.Id] = message.Command.ToResource();
            }

            _debouncer.Execute();
        }
    }

    private void SendUpdates()
    {
        lock (_pendingUpdates)
        {
            var pendingUpdates = _pendingUpdates.Values.ToArray();
            _pendingUpdates.Clear();

            foreach (var pendingUpdate in pendingUpdates)
            {
                BroadcastResourceChange(ModelAction.Updated, pendingUpdate);

                if (pendingUpdate.Name == typeof(MessagingCleanupCommand).Name.Replace("Command", "") &&
                    pendingUpdate.Status == CommandStatus.Completed)
                {
                    BroadcastResourceChange(ModelAction.Sync);
                }
            }
        }
    }
}
