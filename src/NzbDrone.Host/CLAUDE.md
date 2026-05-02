# NzbDrone.Host

## Purpose

Application host and bootstrap layer. Owns:
- Process lifecycle (startup → run → graceful shutdown)
- ASP.NET Core configuration (Kestrel + middleware pipeline + DI)
- Endpoint registration (controllers + SignalR hub)
- Application-mode dispatch (interactive console, Windows Service, utility commands)

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Host\`

## Files (~12 cs files)

| File | Purpose |
|------|---------|
| `Bootstrap.cs` | Startup flow control. Sets up logging, parses startup context, decides ApplicationMode, builds the host. ~84 lines. |
| `Startup.cs` | ASP.NET Core `Startup` — `ConfigureServices` (DI registration, ~400 lines) + `Configure` (middleware pipeline, route mapping). |
| `AppLifetime.cs` | App lifecycle hooks (started, stopping, stopped) |
| `RestartableServiceLifetime.cs` | Service restart handling for Windows Service |
| `TerminateApplicationException.cs` | Used to signal graceful shutdown |
| `ApplicationModes.cs` | Enum: Interactive, InstallService, UninstallService, RegisterUrl, Help, etc. |
| `BrowserService.cs` | Open browser automatically on startup |
| `SingleInstancePolicy.cs` | Prevent multiple running instances (uses named mutex) |
| `UtilityModeRouter.cs` | Dispatch utility CLI commands (e.g. `--register-url`) |
| `AccessControl/FirewallAdapter.cs` | Add Windows Defender Firewall rule |
| `AccessControl/RemoteAccessAdapter.cs` | URL ACL on Windows |
| `AccessControl/RemoteAccessException.cs` | Errors thrown by access adapters |

## Startup Flow

```
NzbDrone.Console.ConsoleApp.Main(args)
     │
     ▼
Bootstrap.Start(args)
     │  - Initialize NzbDroneLogger
     │  - Parse StartupContext (--data, --port, --nobrowser, …)
     │  - Determine ApplicationMode
     │  - Run DB migrations early (FluentMigrator runs auto)
     │  - Single-instance check
     │
     ▼  (if Interactive / Service)
Build Kestrel WebHost via ConfigureHostBuilder
     │
     ▼
Startup.ConfigureServices(IServiceCollection services)
     │  - Register all NzbDrone services by convention
     │  - Add controllers, SignalR, Authentication
     │
     ▼
Startup.Configure(IApplicationBuilder app)
     │  - UseSonarrErrorPipeline (global JSON exception handler)
     │  - UseUrlBase / UseLogging / UseCacheHeaders / UseStartingUp
     │  - UseAuthentication / UseAuthorization
     │  - Map controllers + SignalR hub
     │  - Map static frontend (mappers from Sonarr.Http.Frontend)
     │
     ▼
WebHost.Run() — Kestrel listens on configured port (8989 default)
     │
     ▼  (lifecycle)
ApplicationStartedEvent published → Scheduled tasks tick
     │
     ▼  (on shutdown)
ApplicationShutdownRequestedEvent → graceful drain
```

## Dependency Injection

The container is **DryIoc** (configured via Microsoft.Extensions.DependencyInjection). Most services are registered **by convention** through assembly scanning (see `NzbDrone.Common/Composition/`). Only specific overrides are registered explicitly in `Startup.ConfigureServices`.

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // Auto-register all classes by interface convention from NzbDrone.* assemblies
    services.AddNzbDroneServices();

    // ASP.NET Core
    services.AddControllers();
    services.AddSignalR();

    // Auth
    services.AddSonarrAuthentication();
    services.AddSonarrAuthorization();

    // …
}
```

## Middleware Pipeline (Order Matters)

```csharp
public void Configure(IApplicationBuilder app)
{
    app.UseSonarrErrorPipeline();    // Catch + JSON-format exceptions
    app.UseStartingUp();              // Block requests during startup
    app.UseUrlBase();                 // Reverse-proxy URL base
    app.UseLogging();                 // Request/response logging
    app.UseCacheHeaders();            // ETag / Cache-Control
    app.UseIfModified();              // 304 Not Modified
    app.UseVersion();                 // X-Application-Version header
    app.UseBuffering();               // Allow request buffering

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseEndpoints(endpoints =>
    {
        endpoints.MapControllers();
        endpoints.MapHub<MessageHub>("/signalr/messages");
    });

    // Frontend mappers (serve index.html, login, static, etc.)
    app.UseSonarrFrontend();
}
```

## Server Configuration

| Setting | Default | Source |
|---------|---------|--------|
| HTTP Port | 8989 | `config.xml` → `<Port>` |
| SSL Port | 9898 | `config.xml` → `<SslPort>` |
| Bind Address | `*` | `config.xml` → `<BindAddress>` |
| URL Base | (empty) | `config.xml` → `<UrlBase>` (for reverse proxy) |
| API Key | auto-generated | `config.xml` → `<ApiKey>` |

## Single-Instance Policy

Uses a named mutex (`Sonarr-Mutex`). If a second instance starts with `--terminate`, it signals the first to exit; otherwise it errors out.

## ApplicationModes

| Mode | Trigger |
|------|---------|
| `Interactive` | Default (running from terminal) |
| `InstallService` | `--install-service` |
| `UninstallService` | `--uninstall-service` |
| `RegisterUrl` | `--register-url` (Windows URL ACL) |
| `Help` | `--help` |

## Manga Adaptation Notes

This project is mostly **infrastructure** and reusable as-is. Items to revisit:
- The mutex name `Sonarr-Mutex` — change to `Mangarr-Mutex` when rebranding (otherwise Sonarr installs would conflict).
- `BrowserService` opens `http://localhost:8989` — same path, no change needed.
- Default API path prefix and project naming are still `Sonarr.*` — these will rename in a future migration step.

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) — Overall architecture
- [NzbDrone.Console/CLAUDE.md](../NzbDrone.Console/CLAUDE.md) — Calls `Bootstrap.Start`
- [Sonarr.Http/CLAUDE.md](../Sonarr.Http/CLAUDE.md) — Middleware + REST base used here
- [Sonarr.Api.V5/CLAUDE.md](../Sonarr.Api.V5/CLAUDE.md) — Controllers mapped via `MapControllers`
- [NzbDrone.SignalR/CLAUDE.md](../NzbDrone.SignalR/CLAUDE.md) — Hub registered here
