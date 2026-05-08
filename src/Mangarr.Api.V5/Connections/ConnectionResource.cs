using Mangarr.Api.V5.Provider;
using NzbDrone.Core.Notifications;

namespace Mangarr.Api.V5.Connections;

public class ConnectionResource : ProviderResource<ConnectionResource>
{
    public string? Link { get; set; }
    public bool OnGrab { get; set; }
    public bool OnDownload { get; set; }
    public bool OnUpgrade { get; set; }
    public bool OnImportComplete { get; set; }
    public bool OnRename { get; set; }
    public bool OnHealthIssue { get; set; }
    public bool IncludeHealthWarnings { get; set; }
    public bool OnHealthRestored { get; set; }
    public bool OnApplicationUpdate { get; set; }
    public bool OnManualInteractionRequired { get; set; }
    public bool SupportsOnGrab { get; set; }
    public bool SupportsOnDownload { get; set; }
    public bool SupportsOnUpgrade { get; set; }
    public bool SupportsOnImportComplete { get; set; }
    public bool SupportsOnRename { get; set; }
    public bool SupportsOnHealthIssue { get; set; }
    public bool SupportsOnHealthRestored { get; set; }
    public bool SupportsOnApplicationUpdate { get; set; }
    public bool SupportsOnManualInteractionRequired { get; set; }
    public string? TestCommand { get; set; }
}

public class ConnectionResourceMapper : ProviderResourceMapper<ConnectionResource, NotificationDefinition>
{
    public override ConnectionResource ToResource(NotificationDefinition definition)
    {
        var resource = base.ToResource(definition);

        resource.OnGrab = definition.OnGrab;
        resource.OnDownload = definition.OnDownload;
        resource.OnUpgrade = definition.OnUpgrade;
        resource.OnImportComplete = definition.OnImportComplete;
        resource.OnRename = definition.OnRename;
        resource.OnHealthIssue = definition.OnHealthIssue;
        resource.IncludeHealthWarnings = definition.IncludeHealthWarnings;
        resource.OnHealthRestored = definition.OnHealthRestored;
        resource.OnApplicationUpdate = definition.OnApplicationUpdate;
        resource.OnManualInteractionRequired = definition.OnManualInteractionRequired;
        resource.SupportsOnGrab = definition.SupportsOnGrab;
        resource.SupportsOnDownload = definition.SupportsOnDownload;
        resource.SupportsOnUpgrade = definition.SupportsOnUpgrade;
        resource.SupportsOnImportComplete = definition.SupportsOnImportComplete;
        resource.SupportsOnRename = definition.SupportsOnRename;
        resource.SupportsOnHealthIssue = definition.SupportsOnHealthIssue;
        resource.SupportsOnHealthRestored = definition.SupportsOnHealthRestored;
        resource.SupportsOnApplicationUpdate = definition.SupportsOnApplicationUpdate;
        resource.SupportsOnManualInteractionRequired = definition.SupportsOnManualInteractionRequired;

        return resource;
    }

    public override NotificationDefinition ToModel(ConnectionResource resource, NotificationDefinition? existingDefinition)
    {
        var definition = base.ToModel(resource, existingDefinition);

        definition.OnGrab = resource.OnGrab;
        definition.OnDownload = resource.OnDownload;
        definition.OnUpgrade = resource.OnUpgrade;
        definition.OnImportComplete = resource.OnImportComplete;
        definition.OnRename = resource.OnRename;
        definition.OnHealthIssue = resource.OnHealthIssue;
        definition.IncludeHealthWarnings = resource.IncludeHealthWarnings;
        definition.OnHealthRestored = resource.OnHealthRestored;
        definition.OnApplicationUpdate = resource.OnApplicationUpdate;
        definition.OnManualInteractionRequired = resource.OnManualInteractionRequired;
        definition.SupportsOnGrab = resource.SupportsOnGrab;
        definition.SupportsOnDownload = resource.SupportsOnDownload;
        definition.SupportsOnUpgrade = resource.SupportsOnUpgrade;
        definition.SupportsOnImportComplete = resource.SupportsOnImportComplete;
        definition.SupportsOnRename = resource.SupportsOnRename;
        definition.SupportsOnHealthIssue = resource.SupportsOnHealthIssue;
        definition.SupportsOnHealthRestored = resource.SupportsOnHealthRestored;
        definition.SupportsOnApplicationUpdate = resource.SupportsOnApplicationUpdate;
        definition.SupportsOnManualInteractionRequired = resource.SupportsOnManualInteractionRequired;

        return definition;
    }
}
