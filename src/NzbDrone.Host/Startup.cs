using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

// Sonarr divergence: Phase 15 Plan 15-10 — Mangarr.Api.V5/Series/ DELETED (TV-only).
//   using Mangarr.Api.V5.Series; ← deleted
using DryIoc;
using Mangarr.Api.V5.System;
using Mangarr.Http;
using Mangarr.Http.Authentication;
using Mangarr.Http.ClientSchema;
using Mangarr.Http.ErrorManagement;
using Mangarr.Http.Frontend;
using Mangarr.Http.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using NLog.Extensions.Logging;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Processes;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Host.AccessControl;
using NzbDrone.Http.Authentication;
using NzbDrone.SignalR;
using StackExchange.Profiling;
using IPNetwork = System.Net.IPNetwork;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace NzbDrone.Host
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(b =>
            {
                b.ClearProviders();
                b.SetMinimumLevel(LogLevel.Trace);
                b.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                b.AddFilter("Mangarr.Http.Authentication.ApiKeyAuthenticationHandler", LogLevel.Warning);
                b.AddFilter("Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager", LogLevel.Error);
                b.AddNLog();
            });

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
                options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
                options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
                options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Parse("fc00::"), 7));
                options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Parse("fe80::"), 10));
            });

            services.AddRouting(options => options.LowercaseUrls = true);

            services.AddResponseCompression();

            services.AddCors(options =>
            {
                options.AddPolicy(VersionedApiControllerAttribute.API_CORS_POLICY,
                    builder =>
                    builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader());

                options.AddPolicy("AllowGet",
                    builder =>
                    builder.AllowAnyOrigin()
                    .WithMethods("GET", "OPTIONS")
                    .AllowAnyHeader());
            });

            services
            .AddControllers(options =>
            {
                options.ReturnHttpNotAcceptable = true;
            })

            // Register all controllers from the API and HTTP projects.
            // Sonarr divergence: Phase 15 Plan 15-10 — SeriesLookupController DELETED;
            // SystemController.Assembly (Mangarr.Api.V5) covers all V5 controllers.
            .AddApplicationPart(typeof(SystemController).Assembly)
            .AddApplicationPart(typeof(StaticResourceController).Assembly)
            .AddJsonOptions(options =>
            {
                STJson.ApplySerializerSettings(options.JsonSerializerOptions);
            })
            .AddControllersAsServices();

            services.ConfigureHttpJsonOptions(options =>
            {
                STJson.ApplySerializerSettings(options.SerializerOptions);
            });

            services.AddSwaggerGen(c =>
            {
                // Phase 15 D-12 - V3 SwaggerDoc registration removed (V3 deleted in Plan 15-06).
                c.SwaggerDoc("v5", new OpenApiInfo
                {
                    Version = "5.0.0",
                    Title = "Mangarr",
                    Description = "Mangarr API docs - The v5 API docs apply to Mangarr v1 only.",
                    License = new OpenApiLicense
                    {
                        // Sonarr divergence: Phase 15 close-out — License URL preserved per
                        // D-27 (b) (Sonarr/Sonarr GitHub repo guard); GPL-3.0 inheritance is
                        // factual/legal upstream attribution.
                        Name = "GPL-3.0",
                        Url = new Uri("https://github.com/Sonarr/Sonarr/blob/develop/LICENSE")
                    }
                });

                var apiKeyHeader = new OpenApiSecurityScheme
                {
                    Name = "X-Api-Key",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "apiKey",
                    Description = "Apikey passed as header",
                    In = ParameterLocation.Header,
                };

                c.AddSecurityDefinition("X-Api-Key", apiKeyHeader);

                c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(apiKeyHeader.Name, document)] = new List<string>(),
                });

                var apikeyQuery = new OpenApiSecurityScheme
                {
                    Name = "apikey",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "apiKey",
                    Description = "Apikey passed as query parameter",
                    In = ParameterLocation.Query,
                };

                c.AddServer(new OpenApiServer
                {
                    Url = "{protocol}://{hostpath}",
                    Variables = new Dictionary<string, OpenApiServerVariable>
                    {
                        { "protocol", new OpenApiServerVariable { Default = "http", Enum = new List<string> { "http", "https" } } },
                        { "hostpath", new OpenApiServerVariable { Default = "localhost:8989" } }
                    }
                });

                c.AddSecurityDefinition("apikey", apikeyQuery);

                c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(apikeyQuery.Name, document)] = new List<string>(),
                });

                c.DescribeAllParametersInCamelCase();

                // Generate docs based on the controller's API version
                c.DocInclusionPredicate((docName, apiDesc) =>
                {
                    Type type = null;

                    if (apiDesc.ActionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
                    {
                        type = controllerActionDescriptor.ControllerTypeInfo;
                    }

                    if (type == null)
                    {
                        return false;
                    }

                    var versions = new List<int>();

                    versions.AddRange(type
                        .GetCustomAttributes(true)
                        .OfType<VersionedApiControllerAttribute>()
                        .Select(attr => attr.Version));

                    versions.AddRange(type
                        .GetCustomAttributes(true)
                        .OfType<VersionedFeedControllerAttribute>()
                        .Select(attr => attr.Version));

                    // Return anything with no version or a matching version
                    return !versions.Any() || versions.Any(v => $"v{v}" == docName);
                });
            });

            services
            .AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions = STJson.GetSerializerSettings();
            });

            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Configuration["dataProtectionFolder"]));

            services.AddSingleton<IAuthorizationPolicyProvider, UiAuthorizationPolicyProvider>();
            services.AddSingleton<IAuthorizationHandler, UiAuthorizationHandler>();

            services.AddAuthorization(options =>
            {
                options.AddPolicy("SignalR", policy =>
                {
                    policy.AuthenticationSchemes.Add("SignalR");
                    policy.RequireAuthenticatedUser();
                });

                // Require auth on everything except those marked [AllowAnonymous]
                options.FallbackPolicy = new AuthorizationPolicyBuilder("API")
                .RequireAuthenticatedUser()
                .Build();
            });

            services.AddAppAuthentication();

            services.AddOptions<MiniProfilerOptions>()
                .Configure<IConfigFileProvider>((options, configFileProvider) =>
                {
                    options.RouteBasePath = "/profiler";

                    switch (configFileProvider.Theme)
                    {
                        case "light":
                            options.ColorScheme = ColorScheme.Light;
                            break;
                        case "dark":
                            options.ColorScheme = ColorScheme.Dark;
                            break;
                        default:
                            options.ColorScheme = ColorScheme.Auto;
                            break;
                    }

                    switch (configFileProvider.ProfilerPosition)
                    {
                        case "top-left":
                            options.PopupRenderPosition = RenderPosition.Left;
                            break;
                        case "top-right":
                            options.PopupRenderPosition = RenderPosition.Right;
                            break;
                        case "bottom-left":
                            options.PopupRenderPosition = RenderPosition.BottomLeft;
                            break;
                        default:
                            options.PopupRenderPosition = RenderPosition.BottomRight;
                            break;
                    }

                    options.IgnoredPaths.Add("/MediaCover");
                });

            services.AddMiniProfiler();
        }

        public void Configure(IApplicationBuilder app,
                              IContainer container,
                              IStartupContext startupContext,
                              Lazy<IMainDatabase> mainDatabaseFactory,
                              Lazy<ILogDatabase> logDatabaseFactory,
                              DatabaseTarget dbTarget,
                              ISingleInstancePolicy singleInstancePolicy,
                              InitializeLogger initializeLogger,
                              ReconfigureLogging reconfigureLogging,
                              IAppFolderFactory appFolderFactory,
                              IProvidePidFile pidFileProvider,
                              IConfigFileProvider configFileProvider,
                              IRuntimeInfo runtimeInfo,
                              IFirewallAdapter firewallAdapter,
                              IEventAggregator eventAggregator,
                              MangarrErrorPipeline errorHandler)
        {
            initializeLogger.Initialize();
            appFolderFactory.Register();
            pidFileProvider.Write();

            configFileProvider.EnsureDefaultConfigFile();

            reconfigureLogging.Reconfigure();

            EnsureSingleInstance(false, startupContext, singleInstancePolicy);

            // instantiate the databases to initialize/migrate them
            _ = mainDatabaseFactory.Value;

            if (configFileProvider.LogDbEnabled)
            {
                _ = logDatabaseFactory.Value;
                dbTarget.Register();
            }

            SchemaBuilder.Initialize(container);

            // Phase 33 (COMIX2-01) Plan 33-02 — env-var-gated DryIoc swap of IComixSigner
            // for the offline-tier CassettingComixSigner. Per CONTEXT.md:
            //   D-03: single env-var pair (MANGARR_TEST_CASSETTE_MODE +
            //         MANGARR_TEST_CASSETTE_DIR) drives BOTH the existing HTTP-layer
            //         CassetteHandler (wired in Common/Http/Dispatchers/ManagedHttpDispatcher.cs:171-245)
            //         AND the new signer-layer CassettingComixSigner.
            //   D-04: Replay-mode miss throws InvalidOperationException with miss
            //         message shaped verbatim to CassetteHandler — forces explicit
            //         recording, prevents silent CI gaps.
            //   Production-safety guard: env vars unset / empty / unparseable → the
            //   entire registration block short-circuits and the prior RegisterMany
            //   scan in NzbDrone.Common/Composition/Extensions.cs:29-31 keeps
            //   ComixPlaywrightSigner as the IComixSigner singleton. Production
            //   deployment manifests (Docker image / systemd unit / etc.) do NOT set
            //   MANGARR_TEST_CASSETTE_*; only the test harness does.
            // Mirrors the env-var detection pattern at
            // ManagedHttpDispatcher.cs:175-185 — same var names, same TryParse-with-
            // Trace.WriteLine-on-failure semantics. Mangarr.Core IS the assembly that
            // defines CassettingComixSigner so this uses direct typeof() instead of
            // the reflection-load shape Mangarr.Common needs for the test-assembly hop.
            var comixCassetteMode = Environment.GetEnvironmentVariable("MANGARR_TEST_CASSETTE_MODE");
            var comixCassetteDir = Environment.GetEnvironmentVariable("MANGARR_TEST_CASSETTE_DIR");
            if (!string.IsNullOrEmpty(comixCassetteMode) && !string.IsNullOrEmpty(comixCassetteDir))
            {
                if (Enum.TryParse<CassetteMode>(comixCassetteMode, ignoreCase: true, out var parsedMode))
                {
                    // RegisterDelegate (not Made.Of) — DryIoc's Made.Of-with-lambda compiles
                    // the lambda to an Expression tree, which requires each parameter to be a
                    // ConstantExpression. The closure-captured locals (`comixCassetteDir`,
                    // `parsedMode`) appear in the expression tree as MemberAccess on the C#
                    // compiler-generated `<>c__DisplayClass*` closure type, which trips
                    // DryIoc.Error.UnexpectedExpressionInsteadOfConstantInMadeOf. RegisterDelegate
                    // bypasses the expression-tree analyzer and accepts a plain
                    // Func<IResolverContext, IComixSigner>.
                    //
                    // Resolve the inner ComixPlaywrightSigner LAZILY from the resolver context
                    // (`r.Resolve<...>()`) inside the delegate body — NOT eagerly via a
                    // pre-captured `container.Resolve<ComixPlaywrightSigner>()`. The eager-capture
                    // shape disposed the inner signer before first use: resolving a disposable
                    // singleton during ConfigureServices (before the DryIoc/MS.DI container is
                    // fully built) materializes it in a transient composition scope that gets
                    // disposed when host-build completes, so by request time the captured inner
                    // threw ObjectDisposedException (33-03 LIVE recording surfaced this; the
                    // ComixSignerDryIocResolutionFixture validated registration SHAPE but never
                    // called ProxyFetchAsync on the inner, so the disposal slipped through).
                    // The delegate is Reuse.Singleton, so the inner is resolved exactly once,
                    // after the permanent root singleton scope exists — no premature disposal.
                    container.RegisterDelegate<IComixSigner>(
                        r => new CassettingComixSigner(comixCassetteDir, parsedMode, r.Resolve<ComixPlaywrightSigner>()),
                        reuse: Reuse.Singleton,
                        ifAlreadyRegistered: IfAlreadyRegistered.Replace);
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"MANGARR_TEST_CASSETTE_MODE='{comixCassetteMode}' is not a valid CassetteMode; ignoring (IComixSigner stays as ComixPlaywrightSigner).");
                }
            }

            if (OsInfo.IsNotWindows)
            {
                Console.CancelKeyPress += (sender, eventArgs) => NLog.LogManager.Configuration = null;
            }

            eventAggregator.PublishEvent(new ApplicationStartingEvent());

            if (OsInfo.IsWindows && runtimeInfo.IsAdmin)
            {
                firewallAdapter.MakeAccessible();
            }

            app.UseForwardedHeaders();
            app.UseMiddleware<LoggingMiddleware>();
            app.UsePathBase(new PathString(configFileProvider.UrlBase));
            app.UseExceptionHandler(new ExceptionHandlerOptions
            {
                AllowStatusCode404Response = true,
                ExceptionHandler = errorHandler.HandleException
            });

            app.UseRouting();
            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseResponseCompression();
            app.Properties["host.AppName"] = BuildInfo.AppName;

            app.UseMiddleware<VersionMiddleware>();
            app.UseMiddleware<UrlBaseMiddleware>(configFileProvider.UrlBase);
            app.UseMiddleware<StartingUpMiddleware>();
            app.UseMiddleware<CacheHeaderMiddleware>();
            app.UseMiddleware<IfModifiedMiddleware>();
            app.UseMiddleware<BufferingMiddleware>(new List<string> { "/api/v5/command" });

            app.UseWebSockets();
            app.UseMiniProfiler();

            // Enable middleware to serve generated Swagger as a JSON endpoint.
            if (BuildInfo.IsDebug)
            {
                app.UseSwagger(c =>
                {
                    c.RouteTemplate = "docs/{documentName}/openapi.json";
                });
            }

            app.UseEndpoints(x =>
            {
                x.MapHub<MessageHub>("/signalr/messages").RequireAuthorization("SignalR");
                x.MapPost("/profiler/results", context => Task.CompletedTask).RequireAuthorization("UI");
                x.MapControllers();
            });
        }

        private void EnsureSingleInstance(bool isService, IStartupContext startupContext, ISingleInstancePolicy instancePolicy)
        {
            if (startupContext.Flags.Contains(StartupContext.NO_SINGLE_INSTANCE_CHECK))
            {
                return;
            }

            if (startupContext.Flags.Contains(StartupContext.TERMINATE))
            {
                instancePolicy.KillAllOtherInstance();
            }
            else if (startupContext.Args.ContainsKey(StartupContext.APPDATA))
            {
                instancePolicy.WarnIfAlreadyRunning();
            }
            else if (isService)
            {
                instancePolicy.KillAllOtherInstance();
            }
            else
            {
                instancePolicy.PreventStartIfAlreadyRunning();
            }
        }
    }
}
