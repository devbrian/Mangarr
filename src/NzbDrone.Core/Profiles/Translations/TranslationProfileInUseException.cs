using System.Net;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.Profiles.Translations
{
    // Sonarr divergence: NEW exception per Phase 5 D-01 + Pitfall 8 — see DIVERGENCE.md.
    // Raised by TranslationProfileService.Delete when the profile is assigned to any Manga
    // OR is the global default (Config.DefaultTranslationProfileId). Mirror of
    // QualityProfileInUseException verbatim with type swap.
    public class TranslationProfileInUseException : NzbDroneClientException
    {
        public TranslationProfileInUseException(string name)
            : base(HttpStatusCode.BadRequest, "TranslationProfile [{0}] is in use.", name)
        {
        }
    }
}
