# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
dotnet build                    # Build entire solution
dotnet test                     # Run all tests (~198 tests, xUnit + Moq + FluentAssertions)
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"  # Run single test
dotnet test tests/WorkTracker.Domain.Tests        # Run one test project
```

**EF Core migrations** (use CLI as startup project):
```bash
dotnet ef migrations add MigrationName --project src/WorkTracker.Infrastructure --startup-project src/WorkTracker.CLI
dotnet ef database update --project src/WorkTracker.Infrastructure --startup-project src/WorkTracker.CLI
```

**Publish:**
```bash
dotnet publish src/WorkTracker.Avalonia -c Release -r win-x64
dotnet publish src/WorkTracker.CLI -c Release -r win-x64
```

## Architecture

Clean Architecture with 4 layers + plugin system. Dependencies flow inward only.

```
Presentation (CLI / WPF / Avalonia)
  └─ UI.Shared (shared ViewModels, Orchestrators, Services)
       └─ Application (use cases, IWorkEntryService, Result<T>, DTOs)
            └─ Domain (WorkEntry entity, repository interfaces, business rules)

Plugin.Abstractions (IPlugin, IWorklogUploadPlugin, IWorkSuggestionPlugin, IStatusIndicatorPlugin)
  └─ Referenced by: Application, Infrastructure, individual plugin projects
```

**Key layers:**
- **Domain** — Pure business rules, no dependencies. Defines `IWorkEntryRepository`.
- **Application** — Orchestrates use cases. Uses `Result<T>` for error handling (no exceptions for business logic). Registers services via `DependencyInjection.AddApplication()`.
- **Infrastructure** — EF Core/SQLite, `PluginManager`, repository implementations. Registers everything via `DependencyInjection.AddInfrastructure()` (which calls `AddApplication()` internally).
- **UI.Shared** — Platform-agnostic ViewModels (CommunityToolkit.Mvvm), Orchestrators that coordinate services, `ILocalizationService`, `ISettingsService`.
- **Presentation** — Platform-specific UI. Avalonia (cross-platform), WPF (Windows-only), CLI (Spectre.Console). Each has its own DI setup in `Program.cs` / `App.xaml.cs`.

## Plugin System

Plugins load in isolated `AssemblyLoadContext` from `./plugins/` directory. Three plugin interfaces extend `IPlugin`:

- **`IWorklogUploadPlugin`** — Upload/fetch worklogs (e.g., Tempo)
- **`IWorkSuggestionPlugin`** — Suggest work items (e.g., Jira issues)
- **`IStatusIndicatorPlugin`** — Physical status indicators (e.g., Luxafor LED)

All plugins extend `PluginBase` which provides configuration management with declarative fields, validation (including regex), and `ILogger` injection. Plugin configuration is primarily persisted in the user settings file, with `appsettings.json` (under the `Plugins` section) used only as an initial fallback.

Existing plugins: `Plugin.Atlassian` (Tempo + Jira), `Plugin.Office365Calendar`, `Plugin.GoranG3`, `Plugin.Luxafor`.

## Conventions

- **.NET 10 / C# 13**, nullable reference types enabled, `TreatWarningsAsErrors = true`
- **Central Package Management** via `Directory.Packages.props` — use `<PackageReference Include="..." />` without Version in `.csproj` files
- **Naming**: interfaces `IName`, private fields `_camelCase`, types `PascalCase` (enforced by `.editorconfig`)
- **ViewModels are platform-specific** — WPF and Avalonia each have their own ViewModels. Shared logic goes in `UI.Shared/ViewModels/` or `UI.Shared/Orchestrators/`.
- **DI registration** is centralized: `Application/DependencyInjection.cs` and `Infrastructure/DependencyInjection.cs`. Infrastructure's `AddInfrastructure()` is the single entry point for non-UI services.
- **Database** uses `IDbContextFactory<WorkTrackerDbContext>` (not scoped DbContext). SQLite stored in `%LocalAppData%\WorkTracker\`.
- **Secure storage** for API tokens via `ISecureStorage` (OS credential manager).

## CI/CD

GitHub Actions: `dotnet.yml` (build + test on push/PR to master), `release.yml` (multi-platform publish on `v*` tag push). CI runs on ubuntu-latest.
