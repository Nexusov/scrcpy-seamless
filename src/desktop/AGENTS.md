# Desktop scaffold agent guide

Scope: `src/desktop/`.

Read the root `AGENTS.md`, the active Seamless 2.0 execution plan and the
[Desktop build guide](../../docs/development/desktop-build.md).

Phase 2 owns only the buildable .NET 10 and Avalonia 12.x scaffold. Do not add
device, profile, configuration, ADB or native-host behavior before its owning
phase is authorized. Keep Core independent of presentation and platform effects.

Keep direct NuGet versions in `Directory.Packages.props`, the exact SDK in
`global.json`, and transitive dependency graphs in project lock files. Use
compiled bindings for typed Avalonia views. Verify locked restore, Release build
and headless tests after changing the scaffold.
