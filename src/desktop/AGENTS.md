# Desktop scaffold agent guide

Scope: `src/desktop/`.

Read the root `AGENTS.md`, the active Seamless 2.0 execution plan and the
[Desktop build guide](../../docs/development/desktop-build.md).

Phase 3 adds headless Core device/configuration/connection contracts and
Infrastructure filesystem/ADB adapters. Keep Core independent of Avalonia,
Win32, Process, concrete filesystem and IPC. Phase 5A provides reusable product
Views/ViewModels and explicit, side-effect-free preview composition. Phase 5B
may wire the existing Infrastructure configuration and migration adapters only
at the Desktop composition root: select one explicit data root, keep edits
detached, and use separate revision-checked configuration and Desktop-preference
documents. Preview must remain in-memory and must not construct real adapters.
ADB/native-host integration belongs to Phase 5C; Phase 6 machine IPC remains
separate. Read the
[configuration and boundary guide](../../docs/development/desktop-configuration.md)
before changing v2 persistence, migration, ADB, activation or native-host APIs.

Phase 4 option descriptors under Core are generated from
`spec/options/options.yaml`. Change the spec and run SpecGen; do not hand-edit
generated descriptors. Core owns stable option IDs and resource keys but must
not embed or load localized strings. Generated English option text belongs to
`ScrcpySeamless.Desktop/Resources/Options/` for presentation use. Keep
conditional option validators as named typed Core rules, and preserve unknown
v2 option values while reporting them as unsupported for execution.
For editable native scalar integers and bitrates, validate the parsed base-zero
value shared by ranges and conditional rules while preserving stored spelling.
Keep the legacy catalogue's projected regex fields unchanged.

Keep direct NuGet versions in `Directory.Packages.props`, the exact SDK in
`global.json`, and transitive dependency graphs in project lock files. Use
compiled bindings for typed Avalonia views. Verify locked restore, Release build
and all Core/Infrastructure/Avalonia headless tests after changing these projects.
