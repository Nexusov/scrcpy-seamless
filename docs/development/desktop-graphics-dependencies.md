# Desktop software-renderer servicing boundary

The Windows Desktop keeps Avalonia 12.1.3 and its existing HarfBuzzSharp 8.3.1.3
adapter. It explicitly resolves SkiaSharp and its native companions to 4.155.2
through central versions and locked project graphs. The explicit Linux and WASM
companion constraints prevent the existing Avalonia dependencies from retaining
a second 3.119.4 Skia line; they do not add platform acceptance. The macOS
companion is resolved transitively by the same managed package version.

## Reason and primary evidence

The upstream [servicing overview](https://mono.github.io/SkiaSharp/docs/releases/index.html)
marks 3.119.x unsupported. [Issue 5572](https://github.com/mono/SkiaSharp/issues/5572)
identifies CVE-2026-119261 in private-type SkSL parsing; the 3.119 backport
[PR 5574](https://github.com/mono/SkiaSharp/pull/5574) closed without merging.
The maintainer states that only SkiaSharp 155+ is supported. No stable 3.119.5
was available in the inspected official NuGet index.

The official [4.155.2 release](https://mono.github.io/SkiaSharp/docs/releases/4.155.2.html)
contains the private-type fix and five other listed Skia security repairs.
The official Avalonia 12.1.6 package and source version manifest still request
SkiaSharp 3.119.4, so an Avalonia servicing update alone would not change the
minimum resolved renderer. The selected change keeps the existing UI stack and
does not replace Avalonia, add a GPU/backend or change product shortcuts.

An isolated reproduction called the actual 3.119.4 native compiler before
changing production references. A private uniform type was rejected, but a
private struct field and function parameter were both accepted. The same
three programs were rejected by the actual 4.155.2 native library. This proves
the exercised component defect/correction; the application does not currently
expose an arbitrary SkSL/Lottie input feature, and no application-level exploit,
code execution or historical user failure is asserted.

Skia 4 changes more than that parser. Its
[migration notes](https://mono.github.io/SkiaSharp/docs/releases/4.147.0.html)
include font defaults, path construction and Vulkan constraints. Upstream's
unchanged-ABI statement for 4.155.2 concerns its servicing line, not every
3.x-to-4.x call. Compatibility therefore requires actual calls through the
used Avalonia/Skia/HarfBuzz boundary as well as compilation.

## Executed and retained regression boundary

`ScrcpySeamless.Graphics.Tests` uses the real Skia backend with headless drawing
disabled. It is separate from the existing mock-drawing Desktop tests and never
opens a visible OS window or initializes physical graphics/audio/input devices.
Its eight cases cover:

- the control and both formerly accepted malformed SkSL forms;
- real Avalonia/HarfBuzz LTR and Arabic RTL glyph shaping with usable glyphs
  and positive advances;
- actual text/rounded-control raster pixels and PNG encode/decode;
- native clipping/path drawing and image round-trip pixels;
- rendering the actual isolated preview shell through the software backend.

The isolated old/new probe independently rendered 320x180 controls with the
same sampled color counts while the invalid-type result changed from one of
three rejected to three of three rejected. The committed target checks semantic
pixel bounds instead of treating exact antialiasing counts as a platform contract.
It also verifies that a native Skia 155 library is loaded. Package patch identity
comes from the lock/hash evidence, not that native major-version API alone.

These checks cover used software calls, not every Skia API, physical D3D/OpenGL/
Vulkan driver, monitor/DPI combination, font, malformed image or platform. They
do not transfer previous hardware results to changed managed/native dependencies.
Root integration must run locked restore, Release build and the normal concurrent
full solution suite on the combined source. A future production package must
inventory its actual dependency closure and files independently.

## Package and license provenance

Both managed and Windows-native 4.155.2 NuGet metadata identify source commit
`bae2bd1738735d78625e81cc51c9e60240b6cbd5` and a MIT package license. The lock
files preserve each exact NuGet content hash. The separate matching Win32
package is explicit so the managed assembly cannot silently retain the older
native runtime. All resolved SkiaSharp companions must agree on 4.155.2.

NuGet's MIT declaration does not subsume bundled third-party obligations.
The actual native package also contains `LICENSE.txt` and the upstream
`THIRD-PARTY-NOTICES.txt`; final packaging must copy the corresponding real
package notices, record their hashes/origin and retain other imported runtime
notices. Missing or unidentified notice evidence remains a release gate rather
than an invented SPDX license. The software-renderer tests are not a complete
license audit or a signed/reproducible-build attestation.
