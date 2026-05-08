using System;

// Sonarr divergence: Phase 15 D-08 + BRAND-01 — class renamed (formerly SonarrStartupException) → MangarrStartupException;
// inner-string "Sonarr failed to start" → "Mangarr failed to start" (Task 1 sweep handled the string literals;
// this surgical rename handles the class identifier the \bSonarr\b regex doesn't match). Phase 14 Wave 4 commit
// `3389ad4de` precedent.
namespace NzbDrone.Common.Exceptions
{
    public class MangarrStartupException : NzbDroneException
    {
        public MangarrStartupException(string message, params object[] args)
            : base("Mangarr failed to start: " + string.Format(message, args))
        {
        }

        public MangarrStartupException(string message)
            : base("Mangarr failed to start: " + message)
        {
        }

        public MangarrStartupException()
            : base("Mangarr failed to start")
        {
        }

        public MangarrStartupException(Exception innerException, string message, params object[] args)
            : base("Mangarr failed to start: " + string.Format(message, args), innerException)
        {
        }

        public MangarrStartupException(Exception innerException, string message)
            : base("Mangarr failed to start: " + message, innerException)
        {
        }

        public MangarrStartupException(Exception innerException)
            : base("Mangarr failed to start: " + innerException.Message)
        {
        }
    }
}
