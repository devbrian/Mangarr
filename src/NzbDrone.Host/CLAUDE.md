# NzbDrone.Host

## Purpose

Application host and bootstrap layer. Responsible for starting the web server, configuring dependency injection, and managing the application lifecycle.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Host\`

## Key Components

### Bootstrap
| File | Purpose |
|------|---------|
| `Bootstrap.cs` | Application entry point and startup |
| `Startup.cs` | ASP.NET Core configuration |
| `WebHostController.cs` | Web server lifecycle |

### Server Configuration
| File | Purpose |
|------|---------|
| `ConfigureHostBuilder.cs` | Host builder setup |
| `RouterFactory.cs` | Route configuration |
| `UrlRewriter.cs` | URL rewriting rules |

### Access Control
| File | Purpose |
|------|---------|
| `AccessControl/` | IP/URL access restrictions |

## Startup Flow

```
Program.Main()
    ↓
Bootstrap.Start()
    ↓
Configure Services (DI)
    ↓
Configure Middleware Pipeline
    ↓
Start Kestrel Web Server
    ↓
Run Scheduled Tasks
    ↓
Application Running (port 8989)
```

## Dependency Injection

Services are registered in `Startup.cs`:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // Core services
    services.AddSingleton<IEventAggregator, EventAggregator>();

    // Auto-register by convention
    services.AddNzbDroneServices();

    // API controllers
    services.AddControllers();

    // SignalR
    services.AddSignalR();
}
```

## Middleware Pipeline

```csharp
public void Configure(IApplicationBuilder app)
{
    app.UseStaticFiles();           // Serve UI files
    app.UseRouting();               // Route matching
    app.UseAuthentication();        // Auth middleware
    app.UseAuthorization();         // Authz middleware
    app.UseEndpoints(endpoints =>
    {
        endpoints.MapControllers(); // API routes
        endpoints.MapHub<...>();    // SignalR hubs
    });
}
```

## Server Configuration

Default settings:
- **Port**: 8989 (configurable)
- **Bind Address**: * (all interfaces)
- **SSL**: Optional, configurable
- **Base URL**: Configurable for reverse proxy

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) - Overall architecture
- [NzbDrone.Console/CLAUDE.md](../NzbDrone.Console/CLAUDE.md) - Console app using this
- [Sonarr.Http/CLAUDE.md](../Sonarr.Http/CLAUDE.md) - HTTP middleware
- [Sonarr.Api.V5/CLAUDE.md](../Sonarr.Api.V5/CLAUDE.md) - API controllers
