# NzbDrone.Common

## Purpose

Foundational utility library — used across **all** NzbDrone projects. Contains zero references to other NzbDrone projects, making it the bottom of the dependency graph.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Common\`

**File count**: ~179 .cs files across 23 namespaces.

## Major Namespaces

### Disk Operations (`Disk/`)
| File | Purpose |
|------|---------|
| `IDiskProvider.cs` | Cross-platform file system interface |
| `DiskProviderBase.cs` | Common implementation |
| `DiskTransferService.cs` | File copy/move with verification & disk-space checks |
| `OsPath.cs` | Cross-platform path value type (preserves OS-style separators) |
| `FileSystemLookupService.cs` | Browse directory trees |
| `FileAlreadyExistsException.cs`, `NotParentException.cs`, `DestinationAlreadyExistsException.cs` | Disk-specific exceptions |

Platform-specific overrides live in `NzbDrone.Mono` (Linux/Mac) and `NzbDrone.Windows`.

### HTTP Client (`Http/`)
| File | Purpose |
|------|---------|
| `IHttpClient.cs` / `HttpClient.cs` | HTTP execution, transparent proxy + cookie handling |
| `HttpRequest.cs`, `HttpResponse.cs` | Request/response DTOs |
| `HttpRequestBuilder.cs` / `Factory` | Fluent request building |
| `HttpUri.cs` | URI value type |
| `JsonRpcRequestBuilder.cs`, `XmlRpcRequestBuilder.cs` | RPC convenience |
| `Dispatchers/` | HTTP dispatcher implementations |
| `Proxy/` | HTTP proxy support (ProxySettings, BypassList) |
| `HappyEyeballs/` | RFC 8305 dual-stack connection optimization |
| `HttpException.cs`, `TlsFailureException.cs`, `TooManyRequestsException.cs`, `UnexpectedHtmlContentException.cs` | Domain exceptions |

### Caching (`Cache/`)
| File | Purpose |
|------|---------|
| `ICached.cs` / `Cached.cs` | Generic in-memory cache |
| `ICachedDictionary.cs` / `CachedDictionary.cs` | Cache with auto-expiration |
| `CacheManager.cs` | Factory + lifetime tracking |

### Process Execution (`Processes/`)
| File | Purpose |
|------|---------|
| `IProcessProvider.cs` / `ProcessProvider.cs` | Start/manage external processes |
| `ProcessOutput.cs`, `ProcessOutputLine.cs` | Stdout/stderr capture |

### Environment / Runtime (`EnvironmentInfo/`)
| File | Purpose |
|------|---------|
| `RuntimeInfo.cs` | .NET runtime info |
| `OsInfo.cs` (interface in `IOsInfo.cs`) | Operating system detection |
| `BuildInfo.cs` | Assembly version + build metadata |
| `AppFolderInfo.cs`, `AppFolderFactory.cs` | App data, config, log directories |
| `StartupContext.cs` | Parsed CLI args (`--data`, `--port`, `--nobrowser`, `--terminate`) |

### Logging (`Instrumentation/`)
| File | Purpose |
|------|---------|
| `NzbDroneLogger.cs` | Bootstrap NLog |
| `InitializeLogger.cs` | Apply config |
| `CleansingFileTarget.cs` | NLog target that **scrubs API keys / passwords** from log lines |
| `Layouts/` | Custom NLog layouts |

### Serialization (`Serializer/`)
| Subdirectory | Purpose |
|--------------|---------|
| `Newtonsoft.Json/` | `Json.cs` (Mangarr's main serializer) + converters (`HttpUriConverter`, `UnderscoreStringEnumConverter`, etc.) |
| `System.Text.Json/` | `STJson.cs` + converters for HttpUri, TimeSpan, Utc datetime, Version |

### Threading / Concurrency (`TPL/`)
| File | Purpose |
|------|---------|
| `Debouncer.cs` / `DebounceManager.cs` | Coalesce rapid repeated calls |
| `RateLimitService.cs` | Per-key rate limiting (used by indexers) |
| `LockByIdPool.cs` | Per-key SemaphoreSlim pool |
| `LimitedConcurrencyLevelTaskScheduler.cs` | Bounded TaskScheduler |

### Composition (`Composition/`)
| File | Purpose |
|------|---------|
| `AssemblyLoader.cs` | Reflection-based assembly scanning |
| `Extensions.cs` | DI registration helpers |
| `KnownTypes.cs` | Type allow-list |

### Validation (`EnsureThat/`)
Vendored fluent argument-validation library: `Ensure.That(x).IsNotNull()`, etc. (15 files)

### Extensions (`Extensions/`)
17 files — `StringExtensions`, `IEnumerableExtensions`, `PathExtensions`, `DateTimeExtensions`, `ExceptionExtensions`, `DictionaryExtensions`, etc.

### Other
| Namespace | Contents |
|-----------|---------|
| `Crypto/` | `HashProvider`, `HashConverter` |
| `Exceptions/` | `NzbDroneException` (base), `SonarrStartupException` |
| `OAuth/` | OAuth 1.0a request signing (used by Twitter etc.) |
| `Cloud/` | `SonarrCloudRequestBuilder` (talks to Services.Mangarr.tv) |
| `Options/` | Strongly-typed CLI option groups |
| `Globalization/` | `AdditionalDiacriticsProvider` |
| `Expansive/` | String template expansion (e.g. `${var}`) |
| `Reflection/` | Reflection helpers |
| `Messaging/` | Tiny event interfaces (no implementation) |

## Usage Conventions

### Disk Operations
```csharp
// Always inject IDiskProvider; never use System.IO directly
_diskProvider.FileExists(path);
_diskProvider.CreateFolder(path);
_diskProvider.MoveFile(source, destination);
_diskProvider.GetFiles(folder, SearchOption.AllDirectories);
```

### HTTP Requests
```csharp
var request = new HttpRequestBuilder("https://api.example.com")
    .Resource("v1/items")
    .AddQueryParam("q", searchTerm)
    .SetHeader("X-Api-Key", apiKey)
    .Build();

var response = _httpClient.Get<ItemListResponse>(request);
```

### Logging
```csharp
private readonly Logger _logger;

_logger.Debug("Processing {0}", filename);
_logger.Info("Downloaded {0}", title);
_logger.Warn("Retry attempt {0}", count);
_logger.Error(ex, "Failed to process");
// API keys / passwords in messages are auto-scrubbed by CleansingFileTarget.
```

### Path Handling
```csharp
// Always prefer OsPath for paths that flow across cross-platform code
var p = new OsPath(rawPath);
var combined = p + "subfolder" + "file.ext";
```

## Key Interfaces

```csharp
IDiskProvider          // File system operations
IHttpClient            // HTTP requests
IProcessProvider       // External process execution
ICached<T>             // Generic cache
IAppFolderInfo         // App directories
IRuntimeInfo / IOsInfo // Runtime/OS detection
IRateLimitService      // Per-key throttling
```

## Manga Adaptation Notes

This project is **media-agnostic** — all classes are utilities. **No migration changes needed.** The only Mangarr-specific bits:
- `Cloud/SonarrCloudRequestBuilder.cs` — talks to `services.sonarr.tv`. If Mangarr ever has its own cloud (for metadata/updates), this would be replaced or generalized.
- `Exceptions/SonarrStartupException.cs` — purely a name; rename when project rebrands.

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) — Overall architecture
- [NzbDrone.Core/CLAUDE.md](../NzbDrone.Core/CLAUDE.md) — Heavy consumer of these utilities
- [NzbDrone.Host/CLAUDE.md](../NzbDrone.Host/CLAUDE.md) — Bootstrap uses logging + StartupContext
