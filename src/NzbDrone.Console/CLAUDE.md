# NzbDrone.Console

## Purpose

Console application entry point. This is the **main executable** that starts the application.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Console\`

## Key Files

| File | Purpose |
|------|---------|
| `ConsoleApp.cs` | Application entry (`Main`), exception handling, exit codes (~135 lines) |
| `Mangarr.Console.csproj` | Project file |
| `Mangarr.ico` | App icon |

> **Note**: There is **no `Program.cs`** — `ConsoleApp.cs` contains the static `Main(string[] args)` method directly.

## Entry Point

```csharp
// ConsoleApp.cs (simplified)
public static class ConsoleApp
{
    public static void Main(string[] args)
    {
        // 1. Initialize logging (NzbDroneLogger)
        // 2. Parse command-line StartupContext
        // 3. Call Bootstrap.Start(args)  (from NzbDrone.Host)
        // 4. Catch & log SonarrStartupException, SocketException, IOException, RemoteAccessException
        // 5. Set process exit code
    }
}
```

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Normal shutdown |
| `1` | Unknown failure |
| `2` | Recoverable failure (e.g., port already in use) |
| `3` | Non-recoverable failure (waits for user intervention if interactive) |

## Running

```bash
# From solution root (development)
dotnet run --project src/NzbDrone.Console/Mangarr.Console.csproj

# Compiled output (after build)
./_output/net10.0/Mangarr.Console.exe       # Windows
./_output/net10.0/Mangarr.Console.dll       # via dotnet
```

## Command-Line Arguments

| Argument | Purpose |
|----------|---------|
| `--nobrowser` | Don't open browser on startup |
| `--data=<path>` | Custom data directory (default: `%ProgramData%\Mangarr` on Windows) |
| `--port=<port>` | Custom port (default: 8989) |
| `--terminate` | Terminate other running instance |

Parsed by `StartupContext` in [NzbDrone.Common/EnvironmentInfo/StartupContext.cs](../NzbDrone.Common/EnvironmentInfo/StartupContext.cs).

## Build Output

Compiles to: `_output/net10.0/Mangarr.Console.{exe,dll}` plus all referenced assemblies and the bundled UI in `_output/UI/`.

## Manga Adaptation Notes

- The csproj is named `Mangarr.Console.csproj` and produces `Mangarr.Console.exe`. As part of rebranding, this should eventually be renamed `Mangarr.Console.csproj` / `Mangarr.Console.exe`. Renaming requires updating:
  - Solution file (`src/Mangarr.sln`)
  - Distribution scripts under `distribution/`
  - Service install scripts under `src/ServiceHelpers/`
  - GitHub Actions / build pipelines

## Cross-References

- [NzbDrone.Host/CLAUDE.md](../NzbDrone.Host/CLAUDE.md) — Bootstrap code
- [NzbDrone.Common/CLAUDE.md](../NzbDrone.Common/CLAUDE.md) — StartupContext
- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) — Architecture overview
