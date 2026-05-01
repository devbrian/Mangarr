# NzbDrone.Console

## Purpose

Console application entry point. This is the **main executable** that starts the application.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Console\`

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Application entry point (Main method) |
| `ConsoleApp.cs` | Console-specific initialization |
| `Sonarr.Console.csproj` | Project file |

## Entry Point

```csharp
// Program.cs
public static class Program
{
    public static void Main(string[] args)
    {
        // Parse command line args
        // Initialize logging
        // Start Bootstrap from NzbDrone.Host
        Bootstrap.Start(args);
    }
}
```

## Running

```bash
# From solution root
dotnet run --project src/NzbDrone.Console/Sonarr.Console.csproj

# Or run compiled output
./_output/net10.0/Sonarr.Console.exe
```

## Command Line Arguments

| Argument | Purpose |
|----------|---------|
| `--nobrowser` | Don't open browser on startup |
| `--data=<path>` | Custom data directory |
| `--port=<port>` | Custom port (default: 8989) |

## Build Output

Compiles to: `_output/net10.0/Sonarr.Console.dll`

## Cross-References

- [NzbDrone.Host/CLAUDE.md](../NzbDrone.Host/CLAUDE.md) - Bootstrap code
- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) - Architecture overview
