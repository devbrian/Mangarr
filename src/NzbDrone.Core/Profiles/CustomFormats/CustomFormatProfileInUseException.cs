using System.Net;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.Profiles.CustomFormats
{
    // Sonarr divergence: NEW exception per Phase 5 D-07 + Pitfall 8 — see DIVERGENCE.md.
    public class CustomFormatProfileInUseException : NzbDroneClientException
    {
        public CustomFormatProfileInUseException(string name)
            : base(HttpStatusCode.BadRequest, "CustomFormatProfile [{0}] is in use.", name)
        {
        }
    }
}
