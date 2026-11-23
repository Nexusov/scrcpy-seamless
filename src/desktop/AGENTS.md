# Desktop scaffold agent guide

Settings → Shortcuts edits native keyboard overrides in the existing detached
mirroring option draft. Keep Apply/Cancel and committed launch snapshots; do
not create another preference store. Generate action/default/condition data
from `spec/options/native-shortcuts.json`. The separate control-center editor
retains its own focus scope. Native edits apply only at the next launch, never
through Desktop input injection. Hash-valid older runtime archives can remain
integrity-verifiable while incompatible with current host invocation claims.

Normal no-argument startup defaults to portable storage and validates only the
adjacent package runtime. Installed mode also uses that runtime; DEV keeps its
explicit opt-in runtime selection and isolated root. Preview never resolves
persistent adapters. Do not search the working directory/PATH for a runtime or
substitute the legacy host when the machine bundle is missing/incompatible.

Phase 6A adds Infrastructure framing and JSON codecs. Phase 6B adds the
owned `MachineNativeHost` and typed Core interactive-session observations.
Phase 6C selects it for a compatible explicit device-enabled DEV runtime;
the separate legacy launcher/package stays available. One Desktop owner must
drain the bounded lifecycle stream regardless of the visible page.
The concurrent Sessions workspace retains at most eight independent child
entries. Selection and navigation never transfer ownership; keep one lifecycle
consumer per child and its profile reservation until exact cleanup settles.
Control-center shutdown settles every owned entry, including failed cleanup
and late acquisition. Diagnostics reads those owners' bounded snapshots only.
The explicit DEV evidence action reads an immutable allowlisted snapshot from
that owner; never add a second lifecycle reader or serialize live configuration,
exceptions or process objects. Retain typed completion/cleanup before release.
Preview must remain side-effect free. Exact wire rules live in
../../spec/desktop-native/PROTOCOL.md and process policy in
../../docs/development/desktop-native-runtime.md.
User-requested machine Focus delegates Windows foreground permission only to
the retained live child before command publication; denial remains best effort.
Keep Win32 in Infrastructure and never infer foreground ownership from Applied.

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
Phase 5C attaches explicit DEV-only discovery, pairing/connection, committed-snapshot
launch and a replaceable legacy native adapter. Do not start ADB on page open,
infer USB/network physical identity, launch from dirty drafts, or report a
channel ready from process existence. Close settles owned ADB/native work
after the accepted save/discard decision; preview remains in-memory. Phase 6
machine events must not infer audio/control readiness from video events. Read the
[configuration and boundary guide](../../docs/development/desktop-configuration.md)
before changing v2 persistence, migration, ADB, activation or native-host APIs.
The validated ADB 34.0.5 DEV runtime selects Openscreen only through explicit
Desktop composition settings on child processes that may start the shared
server. Never reset a pre-existing shared ADB server as an automatic repair.

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
