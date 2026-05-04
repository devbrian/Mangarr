using System;
using FluentValidation.Results;
using NLog;

namespace NzbDrone.Core.Notifications.Komga
{
    public interface IKomgaService
    {
        ValidationFailure Test(KomgaNotificationSettings settings);
    }

    // Sonarr divergence: NEW manga sibling per Phase 6 D-15 + D-17 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserService.cs.
    // Test() probes GET /api/v1/libraries to confirm URL + X-API-Key are valid; the returned
    // library list also seeds the Phase 7 Settings UI dropdown.
    public class KomgaService : IKomgaService
    {
        private readonly IKomgaProxy _proxy;
        private readonly Logger _logger;

        public KomgaService(IKomgaProxy proxy, Logger logger)
        {
            _proxy = proxy;
            _logger = logger;
        }

        public ValidationFailure Test(KomgaNotificationSettings settings)
        {
            try
            {
                var libraries = _proxy.GetLibraries(settings);

                if (libraries == null)
                {
                    return new ValidationFailure("Url", "Komga returned no libraries");
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to connect to Komga");
                return new ValidationFailure("Url", $"Unable to connect to Komga: {ex.Message}");
            }
        }
    }
}
