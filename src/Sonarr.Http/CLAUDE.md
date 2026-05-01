# Sonarr.Http

## Purpose

HTTP infrastructure layer providing REST API base classes, middleware, authentication, and error handling. This sits between the API controllers and the host.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Sonarr.Http\`

## Key Components

### REST Infrastructure (`REST/`)
| File | Purpose |
|------|---------|
| `RestController.cs` | Base controller class |
| `RestResource.cs` | Base API resource/DTO |
| `ResourceValidator.cs` | FluentValidation integration |

### Authentication (`Authentication/`)
| File | Purpose |
|------|---------|
| `AuthenticationService.cs` | User authentication |
| `ApiKeyAuthenticationHandler.cs` | API key validation |
| `BasicAuthenticationHandler.cs` | Basic auth support |

### Error Handling (`ErrorManagement/`)
| File | Purpose |
|------|---------|
| `SonarrErrorPipeline.cs` | Global error handling |
| `ApiException.cs` | API-specific exceptions |

### Extensions (`Extensions/`)
| File | Purpose |
|------|---------|
| `RequestExtensions.cs` | Request helpers |
| `ResponseExtensions.cs` | Response builders |

### Validation (`Validation/`)
| File | Purpose |
|------|---------|
| `EmptyCollectionValidator.cs` | Validation rules |
| `UrlValidator.cs` | URL validation |

## Controller Base Pattern

All API controllers inherit from `RestController<TResource>`:

```csharp
public abstract class RestController<TResource> : Controller
    where TResource : RestResource
{
    // Override these for CRUD operations
    protected virtual TResource GetResourceById(int id);
    protected virtual List<TResource> GetResources();
    protected virtual int CreateResource(TResource resource);
    protected virtual void UpdateResource(TResource resource);
    protected virtual void DeleteResource(int id);
}
```

## Resource Pattern

API DTOs inherit from `RestResource`:

```csharp
public class SeriesResource : RestResource
{
    public string Title { get; set; }
    public string Path { get; set; }
    // ... maps from domain model
}
```

## Validation

Uses FluentValidation:

```csharp
public class SeriesResourceValidator : ResourceValidator<SeriesResource>
{
    public SeriesResourceValidator()
    {
        RuleFor(s => s.Title).NotEmpty();
        RuleFor(s => s.Path).IsValidPath();
    }
}
```

## Authentication Flow

1. Request arrives
2. `ApiKeyAuthenticationHandler` checks `X-Api-Key` header
3. Or `BasicAuthenticationHandler` checks Basic auth
4. Authenticated requests proceed to controller
5. Unauthenticated requests get 401 response

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) - Overall architecture
- [Sonarr.Api.V5/CLAUDE.md](../Sonarr.Api.V5/CLAUDE.md) - API controllers using this
- [NzbDrone.Host/CLAUDE.md](../NzbDrone.Host/CLAUDE.md) - Hosts this middleware
