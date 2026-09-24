# Desktop build foundation

Phase 2 introduces a buildable .NET/Avalonia scaffold. It is **not** the
production launcher or a replacement for the 1.x portable package. Device,
configuration, ADB and native-host behavior begin in later phases.

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
contains three projects under `src/desktop/`: Core (future headless policy),
Infrastructure (future platform effects, referencing Core), and Desktop
(Avalonia presentation/composition, referencing both). The test project under
`tests/desktop/` loads the actual placeholder XAML window through Avalonia's
headless platform and checks its compiled binding. No hardware is needed.

`Directory.Build.props` enables nullable analysis, SDK analyzers,
warnings-as-errors, deterministic compilation and package lock files.
`Directory.Packages.props` pins direct NuGet dependencies centrally; checked-in
`packages.lock.json` files lock their transitive graphs. CI should use
`restore --locked-mode`. Avalonia 12 enables compiled bindings by default; the
placeholder view explicitly provides `x:DataType`. Future views must do the
same where the data type is statically known.

The currently newer xUnit v3 4.0.1 cannot be substituted alone: with
Avalonia.Headless.XUnit 12.1.3, test discovery failed with a
`MissingMethodException` in `AvaloniaFactDiscoverer`. The pinned xUnit v3
3.2.2 passes. Upgrade the headless adapter and xUnit together only after a
real compatibility check.

The Desktop scaffold declares the `win-x64` runtime identifier, so the locked
restore above prepares a self-contained Windows publish graph. Its local
publish command is:

```powershell
& $sdk publish ./src/desktop/ScrcpySeamless.Desktop/ScrcpySeamless.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore --output ./dist/desktop-scaffold
```

This command passed locally in Phase 2 and created
`dist/desktop-scaffold/ScrcpySeamless.Desktop.exe`. It is a development output
only. It does not replace the current portable package or authorize a release.
Runtime/package integration and distribution-license review belong to later
phases.
