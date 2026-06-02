using System;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Test.Common
{
    public abstract class LoggingTest
    {
        protected static readonly Logger TestLogger = NzbDroneLogger.GetLogger("TestLogger");

        protected static void InitLogging()
        {
            new StartupContext();

            // NLog lazily auto-loads its configuration from standard locations on
            // the first logging call of a process. Assigning LogManager.Configuration
            // programmatically does NOT suppress that lazy load — so the first real
            // log call in a fresh process triggers the auto-load, which (finding no
            // NLog.config) installs an empty configuration and silently discards the
            // ExceptionVerification warn-capture rule we register below.
            //
            // In the full suite this self-heals (the auto-load fires once on an early
            // test, after which every later rebuild sticks), but a fixture run in
            // isolation gets its warn-capture wiped before the test body executes —
            // ExpectedWarns(n) then sees 0 warns. See issue #299.
            //
            // Force the auto-load to settle BEFORE we build our configuration so our
            // rules are the final word and won't be replaced on first use.
            ForceConfigurationAutoLoad();

            if (LogManager.Configuration == null || LogManager.Configuration.AllTargets.None(c => c is ExceptionVerification))
            {
                LogManager.Configuration = new LoggingConfiguration();

                Enum.TryParse<TestLogOutput>(Environment.GetEnvironmentVariable("SONARR_TESTS_LOG_OUTPUT"), out var logOutput);

                RegisterSentryLogger();

                switch (logOutput)
                {
                    case TestLogOutput.Console:
                        RegisterConsoleLogger();
                        break;
                    case TestLogOutput.File:
                        RegisterFileLogger();
                        break;
                }

                RegisterExceptionVerification();

                LogManager.ReconfigExistingLoggers();
            }
        }

        // Forces NLog to materialize its effective configuration now. The deferred
        // auto-load fires on a logger's first level evaluation (not on the LogFactory
        // Configuration getter), so we trigger it with a logger level check. Doing
        // this up front (before we install our rules) means the load has already
        // settled and won't re-fire on the first real log call to discard the
        // ExceptionVerification warn-capture rule (issue #299).
        private static void ForceConfigurationAutoLoad()
        {
            _ = TestLogger.IsWarnEnabled;
        }

        private static void RegisterConsoleLogger()
        {
            var consoleTarget = new ConsoleTarget { Layout = "${date:format=HH\\:mm\\:ss.f} ${level}: ${message} ${exception}" };
            LogManager.Configuration.AddTarget(consoleTarget.GetType().Name, consoleTarget);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, consoleTarget));
        }

        private static void RegisterFileLogger()
        {
            const string layout = @"${level}|${message}${onexception:inner=${newline}${newline}${exception:format=ToString}${newline}}";

            var fileTarget = new FileTarget();

            fileTarget.Name = "Test File Logger";
            fileTarget.FileName = Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestLog.txt");
            fileTarget.AutoFlush = false;
            fileTarget.KeepFileOpen = true;
            fileTarget.ConcurrentWrites = true;
            fileTarget.ConcurrentWriteAttemptDelay = 50;
            fileTarget.ConcurrentWriteAttempts = 10;
            fileTarget.Layout = layout;

            LogManager.Configuration.AddTarget(fileTarget.GetType().Name, fileTarget);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, fileTarget));
        }

        private static void RegisterSentryLogger()
        {
            // Register a null target for sentry logs, so they aren't caught by other loggers.
            var loggingRuleSentry = new LoggingRule("Sentry", LogLevel.Debug, new NullTarget()) { Final = true };
            LogManager.Configuration.LoggingRules.Insert(0, loggingRuleSentry);
        }

        private static void RegisterExceptionVerification()
        {
            var exceptionVerification = new ExceptionVerification();
            LogManager.Configuration.AddTarget("ExceptionVerification", exceptionVerification);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Warn, exceptionVerification));
        }

        [SetUp]
        public void LoggingTestSetup()
        {
            InitLogging();
            ExceptionVerification.Reset();
            TestLogger.Info("--- Start: {0} ---", TestContext.CurrentContext.Test.FullName);
        }

        [TearDown]
        public void LoggingDownBase()
        {
            // can't use because of a bug in mono with 2.6.2,
            // https://bugs.launchpad.net/nunitv2/+bug/1076932
            if (BuildInfo.IsDebug && TestContext.CurrentContext.Result.Outcome == ResultState.Success)
            {
                ExceptionVerification.AssertNoUnexpectedLogs();
            }

            TestLogger.Info("--- End: {0} ---", TestContext.CurrentContext.Test.FullName);
        }
    }
}
