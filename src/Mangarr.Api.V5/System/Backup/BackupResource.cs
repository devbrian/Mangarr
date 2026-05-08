using NzbDrone.Core.Backup;
using Sonarr.Http.REST;

namespace Mangarr.Api.V5.System.Backup;

public class BackupResource : RestResource
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public BackupType Type { get; set; }
    public long Size { get; set; }
    public DateTime Time { get; set; }
}
