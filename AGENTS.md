# Repository Guidelines

## Project Structure & Module Organization
`w32banish` is a small .NET 8 Windows Forms tray app. The entry point is `Program.cs`, which starts the application context. Core behavior lives in `TrayApp.cs`, including Win32 hooks, tray menu actions, startup registration, and cursor hide/show logic. Build metadata is in `w32banish.csproj`. Generated outputs land in `bin/` and `obj/`; published binaries currently appear in `publish/` and should be treated as build artifacts, not hand-edited source.

## Build, Test, and Development Commands
Use the .NET SDK from the repository root:

```powershell
dotnet build -c Debug
dotnet build -c Release
dotnet run -c Debug
dotnet publish -c Release -r win-x64 --self-contained
```

`dotnet build -c Debug` compiles the local development build. `dotnet build -c Release` produces the optimized standard build for validation before publishing. `dotnet run -c Debug` starts the tray application directly from source. `dotnet publish` produces a distributable Windows executable; use `-o publish` if you want a clean, explicit output folder.

## Coding Style & Naming Conventions
Follow the existing C# style in this repo: 4-space indentation, file-scoped namespace, `PascalCase` for types and methods, `_camelCase` for private fields, and `ALL_CAPS` for Win32 constants. Keep interop declarations grouped and aligned for readability. Nullable reference types are enabled, so new code should be null-safe and avoid suppressions unless justified. Prefer concise comments only where Win32 behavior or hook lifetimes are non-obvious.

## Testing Guidelines
There is no separate test project yet. For now, validate changes with both `dotnet build -c Debug` and `dotnet build -c Release`, then do manual Windows checks: tray icon appears, cursor hides on typing, restores on real mouse movement, and pause/startup menu actions behave correctly. If automated tests are added later, place them in a dedicated `*.Tests` project and mirror the production class or feature names.

## Commit & Pull Request Guidelines
Recent commits use short, imperative subjects such as `Add pause/resume...` and `Fix re-entrancy...`. Follow that pattern: start with a verb, describe the behavioral change, and keep the subject line specific. Pull requests should include a brief summary, manual test notes, and screenshots only when UI or tray menu behavior changes. Link the relevant issue when one exists.

## Windows-Specific Notes
This project depends on Windows APIs such as `user32.dll`, `Magnification.dll`, and registry startup entries under `HKCU`. Test on Windows with the .NET 8 SDK installed; non-Windows environments are not valid runtime targets.
