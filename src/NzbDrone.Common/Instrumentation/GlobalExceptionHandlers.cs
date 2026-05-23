using System;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Common.Instrumentation
{
    public static class GlobalExceptionHandlers
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(GlobalExceptionHandlers));
        public static void Register()
        {
            AppDomain.CurrentDomain.UnhandledException += HandleAppDomainException;
            TaskScheduler.UnobservedTaskException += HandleTaskException;
        }

        private static void HandleTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            var exception = e.Exception;

            if (exception.InnerException is ObjectDisposedException disposedException && disposedException.ObjectName == "System.Net.HttpListenerRequest")
            {
                // We don't care about web connections
                return;
            }

            // PuppeteerSharp's internal response-body cache occasionally throws on 3xx redirect
            // responses (the body is structurally unavailable). The throw escapes the internal
            // handler and surfaces here as an unobserved task. It does not affect the comix
            // signer's correctness — the navigation that triggered the redirect completes — but
            // the noisy stack trace alarms users. Filtered by type-name (not reference) because
            // NzbDrone.Common is dependency-free per its CLAUDE.md.
            //
            // Only suppress when the FLATTENED inner-exception set contains exactly ONE
            // PuppeteerSharp redirect-body exception. Mixed AggregateException payloads
            // (the PuppeteerException AND a real bug) must surface normally — using
            // exception.InnerException (singular) on an AggregateException is not
            // deterministically "the first" inner and would silently swallow real failures.
            if (exception is AggregateException aggregateException)
            {
                var innerExceptions = aggregateException.Flatten().InnerExceptions;
                if (innerExceptions.Count == 1
                    && innerExceptions[0].GetType().FullName == "PuppeteerSharp.PuppeteerException"
                    && innerExceptions[0].Message == "Response body is unavailable for redirect responses")
                {
                    e.SetObserved();
                    return;
                }
            }

            Console.WriteLine("Task Error: {0}", exception);
            Logger.Error(exception, "Task Error");
        }

        private static void HandleAppDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;

            if (exception == null)
            {
                return;
            }

            if (exception is NullReferenceException &&
                exception.ToString().Contains("Microsoft.AspNet.SignalR.Transports.TransportHeartbeat.ProcessServerCommand"))
            {
                Logger.Warn("SignalR Heartbeat interrupted");
                return;
            }

            Console.WriteLine("EPIC FAIL: {0}", exception);
            Logger.Fatal(exception, "EPIC FAIL.");
        }
    }
}
