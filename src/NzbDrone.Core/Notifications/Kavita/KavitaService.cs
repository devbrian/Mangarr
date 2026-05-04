using System;
using FluentValidation.Results;
using NLog;

namespace NzbDrone.Core.Notifications.Kavita
{
    public interface IKavitaService
    {
        ValidationFailure Test(KavitaNotificationSettings settings);
    }

    // Sonarr divergence: NEW manga sibling per Phase 6 D-16 + D-17 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserService.cs.
    // Test() forces a fresh JWT fetch via _proxy.Test(); a thrown exception surfaces as a
    // ValidationFailure on the Url field for the Settings UI to render.
    public class KavitaService : IKavitaService
    {
        private readonly IKavitaProxy _proxy;
        private readonly Logger _logger;

        public KavitaService(IKavitaProxy proxy, Logger logger)
        {
            _proxy = proxy;
            _logger = logger;
        }

        public ValidationFailure Test(KavitaNotificationSettings settings)
        {
            try
            {
                _proxy.Test(settings);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to connect to Kavita");
                return new ValidationFailure("Url", $"Unable to connect to Kavita: {ex.Message}");
            }
        }
    }
}
