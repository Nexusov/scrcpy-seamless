# Option metadata and generation

Phase 4 makes [the option specification](../../spec/options/options.yaml) the canonical source of static option metadata. Its [machine-readable schema](../../spec/options/options.schema.json) has its own spec version; that version is independent of the product version, native scrcpy version and `configuration.v2.json` schema. The initial inventory is the current Seamless native CLI based on [Genymobile/scrcpy v4.0 at `9c0c299ea2628d858012f66371eb2fc70988ba07`](https://github.com/Genymobile/scrcpy/tree/9c0c299ea2628d858012f66371eb2fc70988ba07), with Seamless-specific additions and legacy Windows availability kept explicit. Native help text derived from upstream remains under its original Apache-2.0 attribution; see [third-party notices](../../THIRD_PARTY.md) and [native changes](../CHANGES.md). Phase 9 owns later upstream option ports.

## Ownership and consumers

The pinned [SpecGen project](../../tools/ScrcpySeamless.SpecGen/) validates the spec and generates the [native option-table include](../../src/scrcpy/app/src/cli_options.generated.inc), [Core option descriptors](../../src/desktop/ScrcpySeamless.Core/Options/GeneratedOptionCatalog.g.cs), [English option resources](../../src/desktop/ScrcpySeamless.Core/Options/GeneratedOptionResources.en.json), the 1.x [`option-catalog.json`](../../launcher/option-catalog.json), the [option reference](../reference/options.md), and the schema. The generated C table owns only static declarations: names, short aliases, argument cardinality/hints and native help. The native parser and runtime handling remain hand-written and authoritative for actual device capabilities. The legacy PowerShell loader, editor and settings store continue consuming the same JSON array shape and fields until the approved launcher transition; do not edit that generated file directly.

Stable option IDs and resource keys are invariant across locales. Current English text comes from the spec and generated resources; future UI code should resolve keys instead of embedding English labels in ViewModels. Static classifications distinguish editable settings, Seamless-managed controls, informational actions and unavailable options. An action is never a saved mirroring override. Static build requirements and legacy reconnect limits do not assert that a connected device supports a feature, and the temporary 1.x `SeamlessCompatible` output is not a permanent 2.0 rule.

Complex conditional semantics are implemented by named typed `RuleId` validators, not executable YAML. Examples include camera-source combinations, recording/playback constraints and virtual-display requirements. The temporary 1.x reconnect restrictions remain in the legacy catalogue/store; they are not permanent Core rules. The desktop validation layer returns machine-readable diagnostics for early feedback; native remains the final runtime authority. `MirroringPreferences.Options` retains its bool/string JSON shape. A stored unknown legacy option stays in configuration and produces an explicit unknown/unsupported diagnostic instead of being silently dropped or executed.

## Regeneration and verification

From the repository root, use the pinned SDK as described in the [Desktop build guide](desktop-build.md), then restore the solution in locked mode and run:

```powershell
$sdk = ./scripts/bootstrap-dotnet.ps1
& $sdk restore ./ScrcpySeamless.slnx --locked-mode
& $sdk run --project ./tools/ScrcpySeamless.SpecGen/ScrcpySeamless.SpecGen.csproj --configuration Release --no-restore -- generate
& $sdk run --project ./tools/ScrcpySeamless.SpecGen/ScrcpySeamless.SpecGen.csproj --configuration Release --no-restore -- verify
```

`generate` writes deterministic tracked outputs; a second run with unchanged inputs must produce identical bytes. `verify` is read-only, validates references and metadata constraints, then fails if any committed generated file differs. It does not depend on machine paths, current culture, timestamps, filesystem order or untracked local artifacts. The Windows Desktop CI job runs it after locked restore; the native job compiles the committed C include. The legacy PowerShell suite tests behavior through the generated catalogue and does not parse `cli.c` text. [DocsCheck](../AGENTS.md) checks repository links; SpecGen checks generated option-reference/resource bytes without parsing human-authored prose.

YamlDotNet 18.1.0 is the pinned YAML parser dependency. It is distributed under the [MIT license](https://github.com/aaubry/YamlDotNet/blob/v18.1.0/LICENSE.txt); its package and license metadata are available at [NuGet](https://www.nuget.org/packages/YamlDotNet/18.1.0).
