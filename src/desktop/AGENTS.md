# Desktop scaffold agent guide

Scope: `src/desktop/`.

Read the root `AGENTS.md`, the active Seamless 2.0 execution plan and the
[Desktop build guide](../../docs/development/desktop-build.md).

Phase 3 adds headless Core device/configuration/connection contracts and
Infrastructure filesystem/ADB adapters. Keep Core independent of Avalonia,
Win32, Process, concrete filesystem and IPC. The Desktop project is still a
placeholder; do not wire Phase 5 UI or Phase 6 native IPC here. Read the
[configuration and boundary guide](../../docs/development/desktop-configuration.md)
before changing v2 persistence, migration, ADB, activation or native-host APIs.

Phase 4 option descriptors under Core are generated from
`spec/options/options.yaml`. Change the spec and run SpecGen; do not hand-edit
generated descriptors. Keep conditional option validators as named typed Core
rules, and preserve unknown v2 option values while reporting them as
unsupported for execution.

Keep direct NuGet versions in `Directory.Packages.props`, the exact SDK in
`global.json`, and transitive dependency graphs in project lock files. Use
compiled bindings for typed Avalonia views. Verify locked restore, Release build
and all Core/Infrastructure/Avalonia headless tests after changing these projects.
