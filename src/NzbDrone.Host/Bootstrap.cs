using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using NLog;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Extensions;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

using PostgresOptions = NzbDrone.Core.Datastore.PostgresOptions;

namespace NzbDrone.Host
{
    public static class Bootstrap
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(Bootstrap));

        // Sonarr divergence: Phase 15 D-06 + D-07 - ASSEMBLIES list flipped from Sonarr.* to Mangarr.* (assembly NAMES,
        // = csproj filename basenames post-Wave-2a/2b rename). Sonarr.Api.V3 entry removed per D-12.
        // Per archived 08-06 + Phase 14 Wave 2b row 4: Bootstrap.cs is the runtime AssemblyLoader source-of-truth;
        // its values must match on-disk Mangarr.X.dll filenames or AssemblyLoader fails silently.
        public static readonly List<string> ASSEMBLIES = new()
        {
            "Mangarr.Host",
            "Mangarr.Core",
            "Mangarr.SignalR",
            "Mangarr.Api.V5",
            "Mangarr.Http"
        };

        public static void Start(string[] args, Action<IHostBuilder> trayCallback = null)
        {
            try
            {
                // Sonarr divergence: Phase 15 close-out (Plan 15-09 fix-forward) — Bootstrap.Start
                // log line was the last user-visible "Starting Sonarr" string in app boot output
                // (caught by Plan 15-09 mangarr-phase-smoke-test stdout grep). Pattern ι sweep
                // missed it because Wave 4 sweep applied case-sensitive \bSonarr\b in src/**/*.cs
                // but this file had been modified by Wave 4 / 15-08 brand sweep — verified the
                // line was NOT touched by the regex (likely batch-list-truncation cf Plan 15-08
                // 7000+ files). Flipped here per fix-forward authority.
                Logger.Info("Starting Mangarr - {0} - Version {1}",
                            Environment.ProcessPath,
                            Assembly.GetExecutingAssembly().GetName().Version);

                var startupContext = new StartupContext(args);
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                var appMode = GetApplicationMode(startupContext);
                var config = GetConfiguration(startupContext);

                if (appMode is not (ApplicationModes.Interactive or ApplicationModes.Service))
                {
                    RunUtilityMode(appMode, startupContext, config);
                    return;
                }

                RunHostUntilShutdown(args, startupContext, appMode, trayCallback);

                Logger.Info("Mangarr has shut down completely");
            }
            catch (InvalidConfigFileException ex)
            {
                throw new MangarrStartupException(ex);
            }
            catch (TerminateApplicationException e)
            {
                Logger.Info(e.Message);
                LogManager.Configuration = null;
            }
        }

        private static void RunUtilityMode(ApplicationModes appMode, StartupContext startupContext, IConfiguration config)
        {
            Logger.Debug("Utility mode: {0}", appMode);

            new HostBuilder()
                .UseServiceProviderFactory(new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules())))
                .ConfigureContainer<IContainer>(c =>
                {
                    c.AutoAddServices(ASSEMBLIES)
                        .AddNzbDroneLogger()
                        .AddDatabase()
                        .AddStartupContext(startupContext)
                        .Resolve<UtilityModeRouter>()
                        .Route(appMode);

                    if (config.GetValue(nameof(ConfigFileProvider.LogDbEnabled), true))
                    {
                        c.AddLogDatabase();
                    }
                    else
                    {
                        c.AddDummyLogDatabase();
                    }
                })
                .ConfigureServices(services =>
                {
                    services.Configure<PostgresOptions>(config.GetSection("Mangarr:Postgres"));
                    services.Configure<AppOptions>(config.GetSection("Mangarr:App"));
                    services.Configure<AuthOptions>(config.GetSection("Mangarr:Auth"));
                    services.Configure<ServerOptions>(config.GetSection("Mangarr:Server"));
                    services.Configure<LogOptions>(config.GetSection("Mangarr:Log"));
                    services.Configure<UpdateOptions>(config.GetSection("Mangarr:Update"));
                })
                .Build();
        }

        private static void RunHostUntilShutdown(string[] args, StartupContext startupContext, ApplicationModes appMode, Action<IHostBuilder> trayCallback)
        {
            Logger.Debug("Starting in {0} mode", trayCallback != null ? "Tray" : appMode.ToString());

            bool shouldRestart;
            do
            {
                var builder = CreateConsoleHostBuilder(args, startupContext);
                trayCallback?.Invoke(builder);

                shouldRestart = RunWithRestartCheck(builder.Build());

                if (shouldRestart)
                {
                    Logger.Info("Application restart requested, reinitializing host");
                    NzbDroneLogger.ResetAllTargets(startupContext, false, true);
                    Thread.Sleep(1000);
                }
            }
            while (shouldRestart);
        }

        public static IHostBuilder CreateConsoleHostBuilder(string[] args, StartupContext context)
        {
            var config = GetConfiguration(context);

            var bindAddress = config.GetValue<string>($"Mangarr:Server:{nameof(ServerOptions.BindAddress)}") ?? config.GetValue(nameof(ConfigFileProvider.BindAddress), "*");
            var port = config.GetValue<int?>($"Mangarr:Server:{nameof(ServerOptions.Port)}") ?? config.GetValue(nameof(ConfigFileProvider.Port), 8989);
            var sslPort = config.GetValue<int?>($"Mangarr:Server:{nameof(ServerOptions.SslPort)}") ?? config.GetValue(nameof(ConfigFileProvider.SslPort), 9898);
            var enableSsl = config.GetValue<bool?>($"Mangarr:Server:{nameof(ServerOptions.EnableSsl)}") ?? config.GetValue(nameof(ConfigFileProvider.EnableSsl), false);
            var sslCertPath = config.GetValue<string>($"Mangarr:Server:{nameof(ServerOptions.SslCertPath)}") ?? config.GetValue<string>(nameof(ConfigFileProvider.SslCertPath));
            var sslKeyPath = config.GetValue<string>($"Mangarr:Server:{nameof(ServerOptions.SslKeyPath)}") ?? config.GetValue<string>(nameof(ConfigFileProvider.SslKeyPath));
            var sslCertPassword = config.GetValue<string>($"Mangarr:Server:{nameof(ServerOptions.SslCertPassword)}") ?? config.GetValue<string>(nameof(ConfigFileProvider.SslCertPassword));
            var logDbEnabled = config.GetValue<bool?>($"Mangarr:Log:{nameof(LogOptions.DbEnabled)}") ?? config.GetValue(nameof(ConfigFileProvider.LogDbEnabled), true);

            var urls = new List<string> { BuildUrl("http", bindAddress, port) };

            if (enableSsl && sslCertPath.IsNotNullOrWhiteSpace())
            {
                urls.Add(BuildUrl("https", bindAddress, sslPort));
            }

            return new HostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseServiceProviderFactory(new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules())))
                .ConfigureLogging(logging =>
                {
                    logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
                })
                .ConfigureContainer<IContainer>(c =>
                {
                    c.AutoAddServices(Bootstrap.ASSEMBLIES)
                        .AddNzbDroneLogger()
                        .AddDatabase()
                        .AddStartupContext(context);

                    if (logDbEnabled)
                    {
                        c.AddLogDatabase();
                    }
                    else
                    {
                        c.AddDummyLogDatabase();
                    }
                })
                .ConfigureServices(services =>
                {
                    services.Configure<PostgresOptions>(config.GetSection("Mangarr:Postgres"));
                    services.Configure<AppOptions>(config.GetSection("Mangarr:App"));
                    services.Configure<AuthOptions>(config.GetSection("Mangarr:Auth"));
                    services.Configure<ServerOptions>(config.GetSection("Mangarr:Server"));
                    services.Configure<LogOptions>(config.GetSection("Mangarr:Log"));
                    services.Configure<UpdateOptions>(config.GetSection("Mangarr:Update"));
                })
                .ConfigureWebHost(builder =>
                {
                    builder.UseConfiguration(config);
                    builder.UseUrls(urls.ToArray());
                    builder.UseKestrel(options =>
                    {
                        if (enableSsl && sslCertPath.IsNotNullOrWhiteSpace())
                        {
                            options.ConfigureHttpsDefaults(configureOptions =>
                            {
                                configureOptions.ServerCertificate = ValidateSslCertificate(sslCertPath, sslKeyPath, sslCertPassword);
                            });
                        }
                    });
                    builder.ConfigureKestrel(serverOptions =>
                    {
                        serverOptions.AllowSynchronousIO = false;
                        serverOptions.Limits.MaxRequestBodySize = null;
                    });
                    builder.UseStartup<Startup>();
                });
        }

        public static ApplicationModes GetApplicationMode(IStartupContext startupContext)
        {
            if (startupContext.Help)
            {
                return ApplicationModes.Help;
            }

            if (OsInfo.IsWindows && startupContext.RegisterUrl)
            {
                return ApplicationModes.RegisterUrl;
            }

            if (OsInfo.IsWindows && startupContext.InstallService)
            {
                return ApplicationModes.InstallService;
            }

            if (OsInfo.IsWindows && startupContext.UninstallService)
            {
                return ApplicationModes.UninstallService;
            }

            // IsWindowsService can throw sometimes, so wrap it
            var isWindowsService = false;
            try
            {
                isWindowsService = WindowsServiceHelpers.IsWindowsService();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to get service status");
            }

            if (OsInfo.IsWindows && isWindowsService)
            {
                return ApplicationModes.Service;
            }

            return ApplicationModes.Interactive;
        }

        private static IConfiguration GetConfiguration(StartupContext context)
        {
            var appFolder = new AppFolderInfo(context);
            var configPath = appFolder.GetConfigPath();

            try
            {
                return new ConfigurationBuilder()
                    .AddXmlFile(configPath, optional: true, reloadOnChange: false)
                    .AddInMemoryCollection(new List<KeyValuePair<string, string>> { new("dataProtectionFolder", appFolder.GetDataProtectionPath()) })
                    .AddEnvironmentVariables()
                    .Build();
            }
            catch (InvalidDataException ex)
            {
                Logger.Error(ex, ex.Message);

                throw new InvalidConfigFileException($"{configPath} is corrupt or invalid. Please delete the config file and Mangarr will recreate it.", ex);
            }
        }

        private static string BuildUrl(string scheme, string bindAddress, int port)
        {
            return $"{scheme}://{bindAddress}:{port}";
        }

        private static X509Certificate2 ValidateSslCertificate(string cert, string key, string password)
        {
            X509Certificate2 certificate;

            try
            {
                var type = X509Certificate2.GetCertContentType(cert);

                if (type == X509ContentType.Cert)
                {
                    certificate = X509Certificate2.CreateFromPemFile(cert, key.IsNullOrWhiteSpace() ? null : key);
                }
                else if (type == X509ContentType.Pkcs12)
                {
                    certificate = X509CertificateLoader.LoadPkcs12FromFile(cert, password, X509KeyStorageFlags.DefaultKeySet);
                }
                else
                {
                    throw new MangarrStartupException($"Invalid certificate type: {type}");
                }
            }
            catch (CryptographicException ex)
            {
                if (ex.HResult == 0x2 || ex.HResult == 0x2006D080)
                {
                    throw new MangarrStartupException(ex,
                        $"The SSL certificate file {cert} does not exist");
                }

                throw new MangarrStartupException(ex);
            }
            catch (Exception ex)
            {
                throw new MangarrStartupException(ex);
            }

            return certificate;
        }

        private static bool RunWithRestartCheck(IHost host)
        {
            var shouldRestart = false;

            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopped.Register(() =>
            {
                var runtimeInfo = host.Services.GetRequiredService<IRuntimeInfo>();
                shouldRestart = runtimeInfo.RestartPending;
            });

            host.Run();
            return shouldRestart;
        }
    }
}
