# Specification agent guide

Scope: `spec/`.

Read the root `AGENTS.md` and [option development guide](../docs/development/options.md) before changing option metadata.

`options/options.yaml` is the canonical source for static CLI and option metadata. Its schema version is independent of product and persisted-configuration versions. Change the spec, not its generated native, Core, legacy launcher, schema, or resource outputs; then regenerate and verify them with the pinned .NET SDK:

```powershell
$sdk = ./scripts/bootstrap-dotnet.ps1
& $sdk restore ./ScrcpySeamless.slnx --locked-mode
& $sdk run --project ./tools/ScrcpySeamless.SpecGen/ScrcpySeamless.SpecGen.csproj --configuration Release --no-restore -- generate
& $sdk run --project ./tools/ScrcpySeamless.SpecGen/ScrcpySeamless.SpecGen.csproj --configuration Release --no-restore -- verify
```

`verify` must be read-only and fail on generated drift. Keep stable option IDs and explicit classification, alias, argument, and resource semantics. Complex conditional behavior belongs to named typed `RuleId` validators in Core; do not encode a programming language in YAML. Preserve upstream help-text attribution and run native, Core, and legacy option tests after spec changes.
