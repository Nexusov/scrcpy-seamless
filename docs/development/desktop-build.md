# Desktop build foundation

Phase 2 introduced the buildable .NET/Avalonia scaffold. Phase 3 adds the
headless [Core and Infrastructure foundation](desktop-configuration.md). It is
**not** the production launcher or a replacement for the 1.x portable package.

## Pinned inputs

| Input | Exact version | Source and role | License |
| --- | --- | --- | --- |
| .NET SDK | 10.0.401, Windows x64 | [Microsoft SDK release](https://dotnet.microsoft.com/en-us/download/dotnet/10.0); build/test/publish toolchain | [Windows .NET Library License](https://github.com/dotnet/core/blob/main/license-information.md) for this binary distribution; bundled notices are authoritative |
| .NET target | `net10.0` | Compile target; SDK and runtime versions are distinct | Same distribution terms apply to self-contained runtime payloads |
| Avalonia, Desktop, FluentTheme | 12.1.3 | [Avalonia packages](https://www.nuget.org/packages/Avalonia/12.1.3); desktop presentation only | MIT |
| Avalonia.Headless.XUnit | 12.1.3 | [NuGet](https://www.nuget.org/packages/Avalonia.Headless.XUnit/12.1.3); in-memory UI smoke test | MIT |
| xUnit v3 Microsoft Testing Platform v2 | 3.2.2 | [NuGet](https://www.nuget.org/packages/xunit.v3.mtp-v2/3.2.2); compatible with the pinned Avalonia headless test adapter | Apache-2.0 |

The official SDK ZIP URL is
`https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip`.
Its SHA-512 from [Microsoft release metadata](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json)
is:

```text
24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430
```

`global.json` requires that exact stable SDK and looks first in the ignored
`work/dotnet-sdk-10.0.401` directory, then in the installed SDKs. A .NET
runtime by itself cannot build the solution. To bootstrap a clean Windows
checkout without an administrator or machine-wide install, run the
[bootstrap script](../../scripts/bootstrap-dotnet.ps1) from the repository
root in PowerShell:

The script reads the exact SDK version from `global.json`; it does not keep
a second version constant. Its pinned SHA-512 must be reviewed and updated
with any future SDK change. A mismatched download is rejected.

```powershell
$sdk = powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/bootstrap-dotnet.ps1
& $sdk --version
```

The final command must print `10.0.401`. The script downloads the official ZIP
only when needed, checks SHA-512 before extraction, rechecks a cached ZIP and
prints the local `dotnet.exe` path. An existing exact SDK installed
elsewhere also works through the `global.json` host fallback. Build outputs,
NuGet caches and the downloaded SDK are local artifacts; do not commit them.

## Build and test

From the repository root, after bootstrapping:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sdk = powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/bootstrap-dotnet.ps1
& $sdk restore ./ScrcpySeamless.slnx --locked-mode
& $sdk build ./ScrcpySeamless.slnx --no-restore --configuration Release
& $sdk test ./ScrcpySeamless.slnx --no-restore --configuration Release
```

For a machine-wide exact SDK, replace `& $sdk` with `dotnet`. The solution
contains three projects under `src/desktop/`: Core (headless domain/application
policy), Infrastructure (ADB/filesystem effects, referencing Core), and Desktop
(Avalonia presentation, currently referencing Core only). Phase 5A intentionally
does not compose Infrastructure: the normal Desktop starts with an honest empty
state and the explicit preview uses fixed in-memory scenarios. `tests/desktop/`
contains Core and Infrastructure suites plus Avalonia.Headless shell, layout,
scenario and settings tests. These require no Android device, ADB/native child,
or personal configuration.

`Directory.Build.props` enables nullable analysis, SDK analyzers,
warnings-as-errors, deterministic compilation and package lock files.
`Directory.Packages.props` pins direct NuGet dependencies centrally; checked-in
`packages.lock.json` files lock their transitive graphs. CI should use
`restore --locked-mode`. Avalonia 12 enables compiled bindings by default; the
Phase 5A views and repeated-item templates provide `x:DataType` where the type
is known.

The currently newer xUnit v3 4.0.1 cannot be substituted alone: with
Avalonia.Headless.XUnit 12.1.3, test discovery failed with a
`MissingMethodException` in `AvaloniaFactDiscoverer`. The pinned xUnit v3
3.2.2 passes. Upgrade the headless adapter and xUnit together only after a
real compatibility check.

The Desktop project declares the `win-x64` runtime identifier, so the locked
restore above prepares a self-contained Windows publish graph. Build the
reviewable Phase 5A DEV preview from a committed source state with:

```powershell
$sourceSha = (git rev-parse --short=8 HEAD).Trim()
$previewDirectory = "./dist/dev/scrcpy-seamless-desktop-p05a-g$sourceSha"
& $sdk publish ./src/desktop/ScrcpySeamless.Desktop/ScrcpySeamless.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore --output $previewDirectory
& "$previewDirectory/ScrcpySeamless.Desktop.exe" --preview --scenario=fallback
```

The explicit `--preview` mode is visibly marked as simulated. Scenarios are
`empty`, `usb`, `fallback`, `unauthorized`, `offline`, `failure`, and `reconnect`;
select them in the UI or pass `--scenario=<id>`. The default theme choice is
System and follows Avalonia's system theme inheritance; `--theme=light` and
`--theme=dark` override it only in memory. `--details=expanded` opens the
synthetic device's Connection details. The preview-only `--ui-scale=1.5`
exercises shared typography and layout metrics without writing a preference;
supported values are `1`, `1.1`, `1.25`, and `1.5`. For a filtered Settings
example, use `--preview --page=settings --search=audio-output-buffer`. Settings
categories use a compact selector at narrow widths. Ctrl+F focuses option
search while Settings is active. Preview mode
does not discover or pair devices, start the native child, read/migrate user
configuration, or send activation to the personal installation. Without
`--preview`, the application shows an empty/not-yet-connected state; real
integration belongs to later Phase 5 slices. The self-contained directory is
a local development artifact, not a release or replacement for a legacy DEV
package. Runtime/package integration and distribution-license review remain
later work.
