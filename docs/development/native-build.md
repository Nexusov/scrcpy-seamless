# Windows native-client source build

Seamless 2.0 retains Meson/Ninja and the GCC/MinGW compiler family for the
scrcpy-derived C client. This Phase 2 path starts from a Git checkout and puts
downloaded tools and generated files under ignored `work/`; no global PATH or
personal portable installation is changed. The exact archive URLs and SHA-256
values are in [`scripts/native-toolchain.json`](../../scripts/native-toolchain.json).
The bootstrap checks every downloaded archive before extraction and verifies
all packaged runtime files against [`release-manifest.json`](../../release-manifest.json).

## Validated inputs

| Input | Exact version and origin | Use and licensing |
| --- | --- | --- |
| Python | 3.13.15 from an independently installed Python runtime | Runs the bootstrap; PSF license. The script checks the exact version but does not install or hash the interpreter. Python 3.12.14, used in the earlier local validation, has no official Windows installer or `setup-python` Windows artifact. |
| w64devkit | 2.9.1 official release; GCC 16.2.0, binutils 2.47.20260726, pkg-config 0.34.0 | Compiler, linker, assembler and resource tools; upstream GCC/binutils terms and runtime exception apply. |
| Meson | 1.12.0 wheel from PyPI | Build-system frontend; Apache-2.0. Installed offline from the verified wheel into `work/native/python`. |
| Ninja | 1.13.2 official release | Build executor; Apache-2.0. |
| SDL | 3.4.8 official MinGW development archive | Headers for the native client; zlib license. Runtime DLL is the reviewed scrcpy 4.0 binary. |
| FFmpeg | 8.1.1 source from ffmpeg.org | Configure generates matching public headers. It does **not** rebuild the packaged LGPL-2.1-or-later DLLs; those remain reviewed imports from scrcpy 4.0. |
| scrcpy runtime | Official Windows x64 v4.0 release archive | Reviewed server, SDL/FFmpeg DLLs and images. The client executable from this archive is replaced by the local build. |
| Android Platform Tools | 34.0.5-10900879 from Google | Reviewed ADB trio for package compatibility; Android notices apply. |

`-Dusb=false` disables direct libusb/AOA/OTG support, so libusb is not a
dependency of this Windows build. Ordinary mirroring over USB uses ADB and is
still supported. This baseline does not attest to a source build of SDL,
FFmpeg, ADB or the package's imported server, nor to bit-identical binaries.
See [third-party notices](../../THIRD_PARTY.md) and
[packaging provenance](../PACKAGING.md) before distributing anything.

## Bootstrap and build from a clean checkout

Install Python **3.13.15** from the official Python distribution first, or use
an existing trusted interpreter of exactly that version. The interpreter is
the one host prerequisite; the bootstrap refuses any other patch version.
From the repository root in PowerShell, replace the interpreter path below
with its actual location:

```powershell
& 'C:\path\to\Python312\python.exe' .\scripts\bootstrap-native.py
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 `
  -ConfigPath .\work\native\build.local.json `
  -BuildDirectory .\work\native\client-build `
  -RuntimeDirectory .\work\native\scrcpy-release\scrcpy-win64-v4.0
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 `
  -ConfigPath .\work\native\build.local.json `
  -BuildDirectory .\work\native\test-build `
  -RuntimeDirectory .\work\native\scrcpy-release\scrcpy-win64-v4.0 `
  -NativeTests
```

The first build uses Meson `debugoptimized`, `compile_server=false`,
`portable=true`, `usb=false` and writes `dist/scrcpy.exe` plus its source/hash
sidecar. `-NativeTests` uses Meson `debug`, builds the native test targets and
runs `meson test`; it does not replace `dist/scrcpy.exe`. Distinct fresh build
directories make clean-build evidence explicit. Meson reconfigures an existing
directory on later invocations. The bootstrap is idempotent for verified
archives and extractions carrying its completion marker; it refuses mismatched
or partially extracted cache contents instead of silently repairing unknown
files. A marker does not attest that cached extracted tools remained unchanged
afterward. Use a fresh cache for independent build evidence.

To build and check the current compatible package, use the reviewed runtime
directory created by bootstrap:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1 `
  -RuntimeDirectory .\work\native\scrcpy-release\scrcpy-win64-v4.0
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1 `
  -ArchivePath .\dist\scrcpy-seamless-win64.zip
```

The resulting ZIP uses the locally built native client, but the server and
dependency DLLs still come from reviewed binary inputs. The separate
[Android server source build](server-build.md) produces an additional artifact;
the legacy packager intentionally retains the imported server until its
manifest hash, compatibility and licensing are reviewed together. Never change
an expected hash merely to get a package through validation.

In a Git checkout, native provenance hashes tracked and nonignored untracked
paths under `src/scrcpy`, including modified working-tree content, using ordinal path
ordering and normalized UTF-8 line endings. Git-ignored Gradle/Meson output is
excluded. Packaging rejects an incomplete build, changed source, changed
executable or mismatched reviewed runtime input. Source/hash provenance does
not establish a reproducible compiler or dependency-build attestation.
