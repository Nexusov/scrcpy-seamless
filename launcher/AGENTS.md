# Legacy launcher agent guide

Scope: `launcher/`.

The PowerShell/WinForms launcher is the production Seamless 1.x application
layer and a temporary compatibility/fallback path during the Seamless 2.0
migration. Do not treat it as the architecture to copy into 2.0.

Read:

- `/AGENTS.md`
- `/docs/ARCHITECTURE.md`
- `/docs/development/debugging.md`
- `/docs/development/git-workflow.md`

## Existing 1.x boundaries

Preserve the current intent unless the active migration Phase explicitly
replaces it:

- `*-view.ps1` owns WinForms controls and presentation/event binding.
- `*-session.ps1` owns state transitions and user-action decisions.
- `*-runtime.ps1` owns background/process side effects.
- `adb-process.ps1` owns the ADB process boundary.
- `configuration-store.ps1` owns current phone-configuration validation and
  persistence.
- `options-store.ps1` owns current mirroring settings persistence/encoding.

`option-catalog.json` is a generated 1.x compatibility projection of
`spec/options/options.yaml`. Do not edit the JSON by hand; run SpecGen
`generate` and `verify` as documented in `docs/development/options.md`, then
run the PowerShell option suites. Keep the existing launcher behavior and
catalogue shape until the approved legacy-removal Phase.

Do not add new business logic to views.

## Migration rule

Do not add new long-lived product architecture to PowerShell merely because it
is faster for one change. During 2.0:

- preserve 1.x behavior;
- add bug fixes and migration seams when required;
- prefer implementing new 2.0 features in the new C# architecture once that
  subsystem exists;
- keep the old launcher working until the approved legacy-removal Phase.

When a feature moves to C#, keep characterization coverage until parity is
demonstrated.

## Concurrency and persistence

The current launcher intentionally protects stale asynchronous work with
generation checks and configuration snapshots. Do not remove these protections
without an equivalent stronger mechanism.

Do not extend the current pattern of holding configuration locks across
external process/network operations. Seamless 2.0 replaces it with short
persistence critical sections and revision/CAS-style semantics.

Pairing codes must never be persisted or logged.

## Testing

Run the root 1.x test command after launcher changes:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

For non-trivial bugs, follow `/docs/development/debugging.md` and add a
regression test that exercises behavior through imported functions and explicit
dependencies. Do not parse source text to extract functions and do not
reimplement production logic inside tests.

## Retirement

When `launcher/` is finally removed from the production path, remove this file
or move any still-valid rules to the new scoped AGENTS files in the same
change. Do not leave stale agent instructions behind.
