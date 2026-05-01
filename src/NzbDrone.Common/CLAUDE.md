# NzbDrone.Common

## Purpose

Shared utilities and infrastructure used across all projects. This is the **foundation layer** with no dependencies on other NzbDrone projects.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Common\`

## Key Components

### Disk Operations (`Disk/`)
| File | Purpose |
|------|---------|
| `IDiskProvider.cs` | File system abstraction |
| `DiskProviderBase.cs` | Cross-platform disk operations |
| `DiskTransferService.cs` | File copy/move operations |
| `OsPath.cs` | Cross-platform path handling |

### HTTP Client (`Http/`)
| File | Purpose |
|------|---------|
| `IHttpClient.cs` | HTTP client interface |
| `HttpClient.cs` | HTTP request execution |
| `HttpRequest.cs` | Request builder |
| `HttpResponse.cs` | Response wrapper |

### Process Management (`Processes/`)
| File | Purpose |
|------|---------|
| `IProcessProvider.cs` | Process execution interface |
| `ProcessProvider.cs` | Start/manage external processes |

### Environment (`EnvironmentInfo/`)
| File | Purpose |
|------|---------|
| `IOsInfo.cs` | OS detection |
| `RuntimeInfo.cs` | Runtime environment info |
| `AppFolderInfo.cs` | Application paths |

### Instrumentation (`Instrumentation/`)
| File | Purpose |
|------|---------|
| `NzbDroneLogger.cs` | Logging configuration |
| `Extensions/` | Logging extensions |

### Serialization (`Serializer/`)
| File | Purpose |
|------|---------|
| `Json.cs` | JSON serialization utilities |
| `IntConverter.cs` | Custom JSON converters |

### Extensions (`Extensions/`)
| File | Purpose |
|------|---------|
| `StringExtensions.cs` | String manipulation |
| `PathExtensions.cs` | Path utilities |
| `EnumerableExtensions.cs` | Collection helpers |
| `DateTimeExtensions.cs` | Date utilities |

### Caching (`Cache/`)
| File | Purpose |
|------|---------|
| `ICached.cs` | Cache interface |
| `Cached.cs` | In-memory caching |
| `CacheManager.cs` | Cache factory |

## Usage Conventions

### Disk Operations
```csharp
// Use IDiskProvider for all file operations
_diskProvider.FileExists(path);
_diskProvider.CreateFolder(path);
_diskProvider.MoveFile(source, destination);
_diskProvider.GetFiles(folder, SearchOption.AllDirectories);
```

### HTTP Requests
```csharp
var request = new HttpRequest(url);
request.Headers.Add("User-Agent", "Sonarr");
var response = _httpClient.Execute(request);
```

### Logging
```csharp
private readonly Logger _logger;

_logger.Debug("Processing {0}", filename);
_logger.Info("Downloaded {0}", title);
_logger.Warn("Retry attempt {0}", count);
_logger.Error(ex, "Failed to process");
```

### Path Handling
```csharp
// Always use OsPath for cross-platform compatibility
var osPath = new OsPath(path);
var combined = osPath + "subfolder" + "file.ext";
```

## Key Interfaces

```csharp
IDiskProvider      // File system operations
IHttpClient        // HTTP requests
IProcessProvider   // External process execution
ICached<T>         // Caching
IAppFolderInfo     // Application directories
```

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) - Overall architecture
- [NzbDrone.Core/CLAUDE.md](../NzbDrone.Core/CLAUDE.md) - Uses these utilities
