# Mangarr.Http

## Purpose

HTTP infrastructure layer — sits between the `Mangarr.Api.V5` controllers and the host. Provides:

- REST controller / resource base classes
- Authentication handlers (cookie / API key / basic / OAuth)
- Middleware pipeline pieces (UrlBase, Logging, Cache, Version, Buffering, IfModified)
- Global error handling (`MangarrErrorPipeline`)
- Frontend serving (mappers for `index.html`, login, static assets, covers, manifest)
- Validation utilities + dynamic schema generation for plugin forms


**File count**: ~70 .cs files across 10 namespaces.

## Key Subdirectories

### REST Infrastructure (`REST/`)
| File | Purpose |
|------|---------|
| `RestController.cs` | Base controller. Default GET / POST / PUT / DELETE wired via attribute routing. |
| `RestControllerWithSignalR.cs` | Variant that auto-broadcasts entity changes to SignalR. |
| `RestResource.cs` | Base DTO with `int Id`. |
| `ResourceValidator.cs` | FluentValidation integration. |
| `Attributes/` | Route attributes (`RestGetById`, `RestPostById`, `RestPutById`, `RestDeleteById`, etc.) |

### Authentication (`Authentication/`)
| File | Purpose |
|------|---------|
| `AuthenticationService.cs` | Main auth service (cookie issuance, login/logout) |
| `AuthenticationController.cs` | `/login`, `/logout` endpoints |
| `ApiKeyAuthenticationHandler.cs` | Validates `X-Api-Key` header and `?apikey=` query |
| `BasicAuthenticationHandler.cs` | HTTP Basic auth |
| `NoAuthenticationHandler.cs` | Pass-through when auth disabled |
| `UiAuthorizationHandler.cs` / `UiAuthorizationPolicyProvider.cs` | UI cookie auth |
| `AuthenticationBuilderExtensions.cs` | Wire-up |

### Middleware (`Middleware/`)
| File | Purpose |
|------|---------|
| `UrlBaseMiddleware.cs` | Strip configured URL base from incoming path (reverse-proxy support) |
| `LoggingMiddleware.cs` | Per-request logging |
| `CacheHeaderMiddleware.cs` | Cache-Control / Expires headers on static + immutable responses |
| `IfModifiedMiddleware.cs` | ETag / 304 Not Modified |
| `VersionMiddleware.cs` | Adds `X-Application-Version` header |
| `StartingUpMiddleware.cs` | Returns 503 / "Starting up" page during boot |
| `BufferingMiddleware.cs` | Enables request buffering for replay/inspection |

### Error Handling (`ErrorManagement/`)
| File | Purpose |
|------|---------|
| `MangarrErrorPipeline.cs` | Global try/catch → maps exceptions to JSON error responses |
| `ApiException.cs` | Base API exception (carries HTTP status + JSON body) |

### Frontend (`Frontend/`)
| File | Purpose |
|------|---------|
| `Mappers/IndexHtmlMapper.cs` | Serves `index.html` for SPA routes |
| `Mappers/LoginHtmlMapper.cs` | Serves login page |
| `Mappers/StaticResourceMapper.cs` | Serves JS / CSS bundle output |
| `Mappers/MediaCoverMapper.cs` | Serves cached manga covers |
| `Mappers/ManifestMapper.cs` | PWA `manifest.json` |
| `Mappers/CacheBreakerProvider.cs` | Asset versioning |
| Other mappers | favicon, robots.txt, backup files, log files |

### Client Schema (`ClientSchema/`)
| File | Purpose |
|------|---------|
| `SchemaBuilder.cs` | Reflects on a Settings class and emits JSON schema for the UI form |
| `FieldMapping.cs` | Maps C# types to UI field types (string, number, password, select, etc.) |

### Validation (`Validation/`)
| File | Purpose |
|------|---------|
| `EmptyCollectionValidator.cs`, `UrlValidator.cs`, `PathValidator.cs`, `IPValidator.cs`, `LanguageValidator.cs`, `IsoLanguageValidator.cs` | Custom FluentValidation rules |

### Other
| Subdirectory | Purpose |
|--------------|---------|
| `Extensions/` | Request/Response extension methods |
| `Ping/` | `/ping` health endpoint |

## Controller Base Pattern

Every API controller inherits from `RestController<TResource>` (or `RestControllerWithSignalR`):

```csharp
[V5ApiController]
public class MangaController : RestControllerWithSignalR<MangaResource, Manga>
{
    [RestGetById]
    public override MangaResource GetResourceById(int id) { /* … */ }

    [RestGet]
    public List<MangaResource> GetAll() { /* … */ }

    [RestPostById]
    public override ActionResult<MangaResource> Create(MangaResource resource) { /* … */ }

    [RestPutById]
    public override ActionResult<MangaResource> Update(MangaResource resource) { /* … */ }

    [RestDeleteById]
    public override void DeleteResource(int id) { /* … */ }
}
```

## Resource Pattern

API DTOs inherit `RestResource`:

```csharp
public class MangaResource : RestResource
{
    public string Title { get; set; }
    public string Path { get; set; }
    public int TranslationProfileId { get; set; }
    // … maps to/from Manga domain model via static extension methods
}
```

Mapping convention is **static extension methods** named `ToResource()` and `ToModel()` (in the same `*ResourceMapper.cs` file).

## Validation

```csharp
public class MangaResourceValidator : ResourceValidator<MangaResource>
{
    public MangaResourceValidator()
    {
        RuleFor(m => m.Title).NotEmpty();
        RuleFor(m => m.Path).IsValidPath();
        RuleFor(m => m.TranslationProfileId).GreaterThan(0);
    }
}
```

## Authentication Flow

1. Incoming request
2. `ApiKeyAuthenticationHandler` checks `X-Api-Key` header / `?apikey=` query / `<ApiKey>` form value
3. If absent + UI auth required, `UiAuthorizationHandler` checks cookie session
4. `BasicAuthenticationHandler` honors HTTP Basic if configured
5. `NoAuthenticationHandler` short-circuits when auth is disabled (loopback only or never)
6. Authenticated → controller; otherwise 401

Auth method (None / Forms / Basic) and required scope (Disabled / Local / Enabled) come from `Configuration.AuthenticationMethod` + `AuthenticationRequired`.

## Error Pipeline

`MangarrErrorPipeline` wraps the request and:
- Maps `ApiException` → its HTTP status + body
- Maps `ValidationException` → 400 with field errors
- Maps `ModelNotFoundException` → 404
- Maps unhandled `Exception` → 500 + generic body, logs full stack

## Manga Adaptation Notes

This project is **infrastructure-only** and reusable as-is. The only Mangarr-specific bit is the project name itself. Controllers in `Mangarr.Api.V5` are the things that need conceptual renaming.

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) — Architecture
- [Mangarr.Api.V5/CLAUDE.md](../Mangarr.Api.V5/CLAUDE.md) — Controllers using these bases (sole REST surface; V3 deleted Phase 15)
- [NzbDrone.Host/CLAUDE.md](../NzbDrone.Host/CLAUDE.md) — Hosts this middleware
