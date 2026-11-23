# Early upstream fix decisions

Phase 2 audit date: 2026-09-24. Baseline and latest stable release identities
are in [BASELINE.md](BASELINE.md). Decisions below apply only to the listed
upstream changes and current fork code. An adopted entry identifies its own
attributed commit and the available validation; none implies hardware proof.

| Upstream fix | Affected Seamless code and applicability | Decision / rationale | Evidence |
| --- | --- | --- | --- |
| [#6911 data race and zero-sized video](https://github.com/Genymobile/scrcpy/pull/6911), commits `612947cd95e9` and `f9d5b37d7832` | The decoder-thread initial-size write and missing zero-size guard applied to the fork; Seamless reconnect needs extra event-payload cleanup. | **ADOPTED** in `ff4e1bf`: initial size moves through an owned SDL event to the main thread; zero-size session/frame dimensions are rejected, and teardown/reconnect drains pending payloads after producer join. | Upstream comparison, native build and 15/15 tests at the isolated checkpoint. The event test verifies payload handoff, not a deterministic race or hardware failover test. |
| [Audio allocation overflow check](https://github.com/Genymobile/scrcpy/commit/61ae825916f6173c852b18b87d2a5f7df7a44a77) | The fork's audio callback allocation used an unchecked sample-count multiplication; `sc_allocarray()` already existed locally. | **ADOPTED** in `3789287`: checked allocation before recording the buffer byte count. | Native build and 16/16 tests, including an overflow/normal-allocation fixture. No audio hardware recovery claim follows from this check. |
| [#6853 initial gamepad detection](https://github.com/Genymobile/scrcpy/pull/6853), commits `907aba2d7920` and `2892d4095a4b` | `src/scrcpy/app/src/scrcpy.c` still puts the loop index into SDL3's joystick-instance-ID field and does not free the returned joystick array. | **DEFER to Phase 9**: applicable correctness/leak fix, but no gamepad acceptance fixture in Phase 2; keep this visible rather than mixing it with source-build changes. Reassess if a focused deterministic test can be added earlier. | Source comparison against SDL3 behavior documented in the upstream PR. |
| [#6830 / #6924 colorspace fixes](https://github.com/Genymobile/scrcpy/compare/v4.0...v4.1), commits `95ae3bb17d67` and `5022743a2a68` | `src/scrcpy/app/src/texture.c` lacks mappings for unspecified and YCGCO color spaces; a device can display incorrect colors. | **DEFER to Phase 9**: applicable visual correctness, but no Phase 2 decoded-frame/color validation; review alongside SDL/FFmpeg adoption and visual fixtures. | Upstream commits and current switch statement. |
| [#6859 encoder constraints](https://github.com/Genymobile/scrcpy/pull/6859), commits `71c6bf4a87a8` and `f457fd4cf5ff` | Server `Size`/`VideoConstraints` still use the v4.0 algorithm. | **DEFER to Phase 9**: broad server behavior change requiring device/capability fixtures; no urgent failure observed in the accepted baseline. | Upstream PR and current Java sources. |
| [#6919 camera-size correction](https://github.com/Genymobile/scrcpy/pull/6922), commit `6f743be989ca` | `server/.../CameraCapture.java` still constrains the filtered output size. | **DEFER to Phase 9**: applicable to camera capture, but changes server rendering/filter behavior beyond the additive build foundation. | Upstream PR and current Java source. |
| [Virtual display size/rotation fixes](https://github.com/Genymobile/scrcpy/compare/v4.0...v4.1), commits `83c0e6a1a241` and `4954415cc348` | `server/.../NewDisplayCapture.java` still constrains the resize with the v4.0 overload and uses stored DPI. | **DEFER to Phase 9**: applicable advanced-mode correctness; no Phase 2 virtual-display device fixture. | Upstream commits and current Java source. |
| [Protocol schema wording](https://github.com/Genymobile/scrcpy/commit/4d79fb5b260a00665f54e3fe618d42d154497a83) | Documentation-only clarification; no wire-format change identified. | **NOT APPLICABLE** as an urgent code port; Phase 9 protocol audit may use the corrected schema. | Upstream commit. |

No v4.1 VP8/VP9 feature, media scan addition or dependency-library upgrade is
adopted by this early fix audit. They require separate Phase 9 or dependency
provenance decisions. The reviewed 1.x imported runtime remains pinned until
an explicit compatibility/provenance transition is tested.

## Autonomous selective texture ports — 2026-11-23

The latest stable release inspected through the official GitHub API is
[v5.0.1](https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1), published
2026-10-08, resolving to
`a60891aea193d92e7e5c3942700eca63f9d19a5f`. This is an audit reference, not a
wholesale source/dependency upgrade. The complete v4.0-to-stable comparison
contains 113 commits; earlier Phase 2 decisions above remain historical.

| Exact upstream source | Adaptation in this fork | Evidence and boundary |
| --- | --- | --- |
| [95ae3bb17d678c3dc0e0f31f50ee5a50ff2c269e](https://github.com/Genymobile/scrcpy/commit/95ae3bb17d678c3dc0e0f31f50ee5a50ff2c269e), [5022743a2a68701b7bde06cefbcc7c77ffc8f59e](https://github.com/Genymobile/scrcpy/commit/5022743a2a68701b7bde06cefbcc7c77ffc8f59e), `app/src/texture.c` | Map unspecified/YCGCO colorspaces to BT709, preserving full/limited range. | Actual texture-property construction failed the expected colorspace assertion before production changes; four mapping cases pass afterward. |
| [730604ffba45cbaabac8336aeb59c44a6c8dc4c9](https://github.com/Genymobile/scrcpy/commit/730604ffba45cbaabac8336aeb59c44a6c8dc4c9), [483de65ea78dc2c22b3f975bf0847afaeb682d9a](https://github.com/Genymobile/scrcpy/commit/483de65ea78dc2c22b3f975bf0847afaeb682d9a), `app/src/texture.c` | Do not destroy borrowed texture properties. Retain the fork's existing texture destruction on failed property lookup instead of reproducing the intermediate upstream error-path leak. | The pre-fix ownership case observed premature destruction of borrowed properties. Current lookup/missing-chroma/update failure cases require exactly one texture release. |
| [562999e9f08a4f3e0ac8bb48b04513ccac2d9c69](https://github.com/Genymobile/scrcpy/commit/562999e9f08a4f3e0ac8bb48b04513ccac2d9c69), `app/src/texture.c` and `.h` | Retain all three YV12 plane texture IDs; apply filtering and generate mipmaps for each plane, with the existing OpenGL/OpenGLES property namespaces. | The pre-fix plane case failed because only one plane received parameters. Current OpenGL and OpenGLES cases verify all three plane operations. |
| [2e41f47c3b76825eff22d9358a3227ca1b561de0](https://github.com/Genymobile/scrcpy/commit/2e41f47c3b76825eff22d9358a3227ca1b561de0), `app/src/texture.h` | Replace the unrelated display header guard with the texture guard. | Mechanical header correction; the linked target compiles with the real production header. |

Original project: Genymobile/scrcpy; the selected commits are authored by Romain
Vimont. The pinned upstream
[LICENSE](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/LICENSE)
is Apache-2.0; the selected files were inspected without conflicting per-file
notices. The destinations remain the corresponding existing upstream-derived
`src/scrcpy/app/src/texture.c` and `.h`; no external assets/dependencies were
added and existing project notices remain. Adaptation preserves the fork's
generation ingress and renderer ownership, using its `ARRAY_LEN` convention.

`test_texture_ports` links actual production texture code, real SDL property
objects and real FFmpeg frame buffers, with controlled renderer/GL external
effects. Its ten internal cases are one registered Meson check, not ten targets
or hardware tests. Three isolated pre-fix invocations retained distinct
ownership/plane/colorspace assertion failures; they are not historical device
incidents. Controlled doubles intentionally prevent a premature property-release
attempt from invalidating fixture storage while recording the ownership breach.
No physical OpenGL/display/device validation is claimed. The full stable audit
and remaining recorder/input/server/dependency findings remain in the local
autonomous evidence; these texture ports do not close those separate items.

## Recorder packet and queue ownership ports — 2026-11-23

Adapted upstream [573efa03c6d826d4e61fa34f35e0c3ed51ed7d0b](https://github.com/Genymobile/scrcpy/commit/573efa03c6d826d4e61fa34f35e0c3ed51ed7d0b)
(`app/src/recorder.c`): release a newly referenced AVPacket if video/audio queue
admission fails. The release occurs after unlocking, so failure does not retain
the payload or execute its destructor under the recorder mutex. Also adapted
the recorder portion of [46c3d43a957b8ec86752fc6c93b5749b2c23ede7](https://github.com/Genymobile/scrcpy/commit/46c3d43a957b8ec86752fc6c93b5749b2c23ede7):
destroy both queue backing allocations in final recorder destruction, after the
existing worker join has cleared packet contents. The already implemented
file-pusher/delay portions were not repeated. No segmentation or recorder
outcome/startup policy was changed by these four production lines.

Original project and author are Genymobile/scrcpy and Romain Vimont; Apache-2.0
rights and selected-file inspection are the same as the pinned stable audit
above. Destination is the existing `src/scrcpy/app/src/recorder.c`; notices and
dependency versions remain unchanged. Tests are project-local fixtures linking
the actual recorder implementation, not a copied lifecycle model.

Three distinct behavioral reds were retained before the fix: rejected video
and rejected audio left two actual AVBuffer references instead of the caller's
one, and actual worker stop/join left both queue backing allocations unreleased
by final destruction. The corrected five-case fixture verifies both rejected
admissions, real worker payload settlement plus backing release, successful init
without worker acquisition, and failed thread start without a bogus join. Real
FFmpeg packet references/final buffer callbacks and a real worker are used;
allocation admission and AVIO/header output are controlled external effects.
An intentionally rejected output header is not a successful recording or a
product failure. No file/device/ADB operation or hardware recording validation
is established. Broader recording/reconnect semantics and other audit findings
remain separate work.

## Initial SDL gamepad instance IDs — 2026-11-23

Adapted upstream [907aba2d792020b72e4f8ce337f7191744237487](https://github.com/Genymobile/scrcpy/commit/907aba2d792020b72e4f8ce337f7191744237487)
from `app/src/scrcpy.c` into the extracted `src/scrcpy/app/src/session.c`:
SDL3 gamepad-added events carry the enumerated joystick instance ID, not the
enumeration index. Genymobile/scrcpy, Romain Vimont, Apache-2.0; existing notices
are preserved. The separate array-release correction is already present.

The existing production-owner fixture now enumerates three non-contiguous IDs,
filters one non-gamepad, observes the two actual queued SDL events and retains
the exact array-release assertion. Before the one-line production correction,
the targeted owner case failed on the first event's incorrect ID. This is a
synthetic enumeration/event regression, not physical gamepad or UHID evidence.

## Explicit texture mipmaps option — 2026-11-23

Adapted upstream [b5cfe182817d8b2bd9b3d2e254c4eb12512a65d3](https://github.com/Genymobile/scrcpy/commit/b5cfe182817d8b2bd9b3d2e254c4eb12512a65d3)
(`app/src/screen.c`) into `src/scrcpy/app/src/screen.c`: video textures enable
mipmaps only when the explicit option also permits them. Original project and
author: Genymobile/scrcpy, Romain Vimont, Apache-2.0; notices remain unchanged.
The production-linked presentation fixture checks all four video/option
combinations at the actual texture-initialization boundary. Its pre-fix red
observed mipmaps enabled for video despite the disabled option. Existing
presentation ownership, frame references and initialization rollback checks are
retained. Controlled window/texture effects do not establish GPU behavior.

## Physical keyboard layout and AltGr ports - 2026-11-23

Adapted upstream [b80edbc71c9e5bfb9aefd140b90af45337b4dbdb](https://github.com/Genymobile/scrcpy/commit/b80edbc71c9e5bfb9aefd140b90af45337b4dbdb)
and [949d952d1843787d56155fd220cf984afd05f5dc](https://github.com/Genymobile/scrcpy/commit/949d952d1843787d56155fd220cf984afd05f5dc)
from `app/src/input_manager.c` and `app/src/shortcut_mod.h` into the matching
`src/scrcpy/app/src/` modules. Genymobile/scrcpy; original authors Vishnu070803
and Romain Vimont; Apache-2.0, existing notices retained.

A known physical scancode now reaches the existing key processor even if the
layout's virtual keycode is unknown. Unknown physical scancodes are still
rejected. SDL MODE modifiers/key releases are normalized consistently as
Right-Alt in device-key delivery and shortcut text suppression. Configured
shortcut choices, presentation readiness and generation detach remain intact.

Two pre-fix production-linked reds are retained: the known physical key was
dropped, and text was wrongly delivered while MODE represented the configured
Right-Alt shortcut modifier. The existing input-binding target now checks those
cases, normalized key modifiers, modifier release, unknown-scancode rejection
and pre-presentation/detached blocking, alongside its earlier input routes.
Synthetic SDL/input traits establish no physical keyboard-layout, clipboard,
UHID/AOA or platform-backend pass.

## Playback capture mixing-rule correction - 2026-11-23

Adapted upstream [8b05a6163c789f27da0ced27bec466f12f813b2e](https://github.com/Genymobile/scrcpy/commit/8b05a6163c789f27da0ced27bec466f12f813b2e)
and [e71cdb8782f524d8ec4deaa1b1e4930c728acab0](https://github.com/Genymobile/scrcpy/commit/e71cdb8782f524d8ec4deaa1b1e4930c728acab0)
from `server/src/main/java/com/genymobile/scrcpy/audio/AudioPlaybackCapture.java`.
Original project: Genymobile/scrcpy; author zkyang (GitHub Jayatubi), committer
Romain Vimont; Apache-2.0. Notices and dependency versions are retained.

The voice-capture allowance is now applied before the immutable rule is built,
and matching includes the upstream's thirteen usages, including GAME. The
existing reflection sequence is extracted into the package-private
`AudioPlaybackRule` helper; capture still constructs Android attributes and
handles reflection errors through its existing exception path. No capture
permission, route, audio lifetime or reconnect policy is changed.

The retained two-test red uses that extracted production sequence with the old
MEDIA-only/late-voice behavior, not an unmodified historical full build. Actual
Gradle/JUnit discovery reports both semantic assertions failing before the fix.
The corrected production helper is tested against an immutable controlled rule
builder, all thirteen distinct usages and unavailable-method propagation. JVM
reflection/order evidence is separate from Android service behavior, audible
readiness and physical-device compatibility; none is claimed by these tests.

## Camera repeating-request stop - 2026-11-23

Adapted upstream [96957c0f5140c8a596dfaf634d4debd972ad2368](https://github.com/Genymobile/scrcpy/commit/96957c0f5140c8a596dfaf634d4debd972ad2368)
from `server/src/main/java/com/genymobile/scrcpy/video/CameraCapture.java` into
the matching destination plus the package-private `CameraRequestStop` effect
boundary. Genymobile/scrcpy, Romain Vimont, Apache-2.0; notices are preserved.
The existing posted camera-thread lambda now requests `stopRepeating()` before
forgetting its current session/request/start state. Already closed/disconnected
session exceptions remain debug observations; unexpected failures propagate.
No Handler, camera-thread ownership or GL release order was redesigned.

Four JVM cases cover one request, absent session, already-closed rejection and
unexpected failure propagation. Three expected red assertions were retained
with the old no-request behavior after extracting the boundary, before adding
the actual request. This is not a pristine historical full build or an executed
camera failure. Tests exercise the production stop-effect helper; source review
separately verifies its position in the real posted callback before field
release. Actual Android Handler/backend execution and the checked framework
CameraAccessException branch are not established by these controlled JVM cases.

## Encoder capabilities and capture geometry - 2026-11-23

Coherently adapted these Genymobile/scrcpy commits, authored by Romain Vimont
under Apache-2.0, retaining existing notices:

- [71c6bf4a87a8eee7f203d71e030375845fff34e2](https://github.com/Genymobile/scrcpy/commit/71c6bf4a87a8eee7f203d71e030375845fff34e2):
  `model/Size.java`, `video/VideoConstraints.java`, `video/SurfaceEncoder.java`
  and new `util/BinarySearch.java`; use coupled encoder capabilities instead of
  treating their two extreme sizes as one maximum rectangle.
- [f457fd4cf5ff4e91853159cbbbbbb98efa8f30b3](https://github.com/Genymobile/scrcpy/commit/f457fd4cf5ff4e91853159cbbbbbb98efa8f30b3):
  apply declared encoder constraints only after an actual failed attempt.
- [856e97dce4c6c2999a5ead6f15ceba45f54bc243](https://github.com/Genymobile/scrcpy/commit/856e97dce4c6c2999a5ead6f15ceba45f54bc243)
  and [6f743be989ca6e003b6e95dcdda169c55e48d30a](https://github.com/Genymobile/scrcpy/commit/6f743be989ca6e003b6e95dcdda169c55e48d30a):
  `video/CameraCapture.java`; after camera selection apply only final alignment,
  and use an intermediate identity GL transform when the surface size differs.
- [83c0e6a1a241a3190bd5c95b7f1b544ee1d05700](https://github.com/Genymobile/scrcpy/commit/83c0e6a1a241a3190bd5c95b7f1b544ee1d05700)
  and [4954415cc34807ea3f086e2bf91106d31f5469cf](https://github.com/Genymobile/scrcpy/commit/4954415cc34807ea3f086e2bf91106d31f5469cf):
  `video/NewDisplayCapture.java`; allow independent resize dimensions and use
  the current DisplayInfo DPI instead of the earlier creation value.

Destinations retain these paths under `src/scrcpy/server/src/main/java/`.
The pinned v5.0.1 source for Size/BinarySearch was inspected at
`a60891aea193d92e7e5c3942700eca63f9d19a5f`. A narrow numeric
`VideoSizeCapabilities` boundary and actual Android delegate allow production
Size/SurfaceEncoder code to be tested without inventing Android framework
objects. The initial extrema behavior was preserved through that extraction:
two behavioral reds then showed a valid square lost by the extreme-rectangle
approximation and declared limits already applied before failure. These are
controlled post-extraction source reproductions, not pristine historical full
builds or executed codec failures.

The upstream sizing algorithm required bounded local adaptations: widened scale
products, an aligned legal minimum, upper-bound search followed by actual size
validation so minimum block counts do not form a false predicate prefix, and
no dependent SDK query with an unsupported no-match fallback. Four additional
numeric reds established those gaps in the adapted upstream path. Expected
unsupported-width queries remain candidate rejection; unexpected SDK failures
propagate. No compatible declared size still uses the explicit validation
fallback, not a claim that every vendor tuple is supported. The actual
[Android 14 capability implementation](https://android.googlesource.com/platform/frameworks/base/+/android-14.0.0_r1/media/java/android/media/MediaCodecInfo.java)
was inspected separately for lower block-count ranges and throwing dependent
queries; no AOSP implementation was copied. BinarySearch uses a local primitive
callback to keep the existing API-21 physical-display route free of the API-24
`java.util.function.Predicate` library dependency.

Retry precedence is explicit and production-tested: before first output,
opt-out fails without applying capabilities; opt-in first tries the declared
capabilities, then the existing smaller-size fallback if rejected. After output,
the original two waits remain; at the original error limit, opt-in may apply one
previously untried capability update, then fails on another error. Opt-out never
adds that update. The original limit and 50-ms timing effect are unchanged;
tests replace only the wait effect. Broken-pipe, transport and Stop handling
are not changed.

Four geometry reds were retained with the extracted old calculations. Tests
exercise the actual camera static calculations and the actual virtual-display
owner's preparation over its current state. The same preparation feeds the
shipping tracker/resize call, keeping logical size, physical rotation and DPI
together. Android Handler, MediaCodec, GL and virtual-display service effects
are not executed by these JVM fixtures. The exact inherited size expectation
400x418 becomes 400x419 because 419 is the largest major dimension whose scaled
width still floors to the supported 400; no tolerance assertion replaces it.

The final relevant fixtures comprise retained Size cases, capability/minimum/
overflow/rejection cases, seven real SurfaceEncoder retry cases, two search
cases and six capture-geometry cases. An independent exhaustive numeric oracle
checks 2507 valid shapes inside one test; orientation combinations likewise are
inside one test. These counts are not additional tests, targets or hardware
passes. Full server unit/build/style results are recorded separately from the
controlled red experiments. No physical codec/camera/display compatibility or
explanation of the historical P7.2 geometry observation follows from this port.

## Stable v5.0.1 changes outside the selected ports

The selective review pins stable
[`a60891aea193d92e7e5c3942700eca63f9d19a5f`](https://github.com/Genymobile/scrcpy/tree/a60891aea193d92e7e5c3942700eca63f9d19a5f)
(v5.0.1), compared with the v4.0 baseline over 113 upstream commits. This is
not an assertion that every upstream commit is necessary for Windows software
decode parity, nor an exhaustive vulnerability/dependency audit.

The hardware-decoder/interop subsystem and its DRM/VAAPI/EGL/AV1 follow-up
repairs are not transplanted into the accepted software-decoder route. Adopting
that subsystem later requires its complete correctness set and backend/driver
validation, rather than isolated patches to code that is currently absent.
VP8/VP9 and the new SCAN_FILE control type 22 likewise remain optional feature
sets: current supported codecs are H264/H265/AV1 and control types are 0..21.
Adding either requires coherent options, codecs, wire/controller, recording
and container coverage. No VPx or media-indexing support is claimed here.

Terminal-title OSC output is not needed by the framed Desktop machine route.
Upstream decoder-worker/video-regulator restructuring overlaps the local
generation/FIFO/ingress model; it is not replayed wholesale. The local model
still requires its own queue and retained-memory proofs. New upstream SDL,
FFmpeg, libusb, dav1d and platform-tools versions require a separate actual-
shipped dependency advisory/license/ABI audit; a source-port review cannot
establish that those imported binaries are free of applicable advisories.

Small residual source differences have narrower dispositions:

- [Device-name static assertion](https://github.com/Genymobile/scrcpy/commit/a351ff666985dd84c026f452bf0872fb1f516dab):
  current `server.c` reads a 64-byte `SC_DEVICE_NAME_FIELD_LENGTH` buffer, forces
  its last byte to NUL, then copies into the same-sized declaration in
  `server.h`. The existing copy is bounded and terminated. The missing upstream
  assertion guards future declaration drift; it is optional compile-time
  hardening, not evidence of a current out-of-bounds read or missing terminator.
- [Locale-root Quest comparison](https://github.com/Genymobile/scrcpy/commit/f498e824b4d12bd02fa5eed3688f043bd5743dcf):
  the current fallback uses default-locale lowercasing of the model name. The
  upstream explicit locale is deterministic hardening. No concrete supported
  Quest-prefix failure has been reproduced; the ASCII prefix contains no
  locale-sensitive I. The existing lint warning is retained, not called fixed.
- [Const-correct strchr pointers](https://github.com/Genymobile/scrcpy/commit/b64b5339eae8e94646a2759006dacf46d4950425):
  these are source qualification differences without a demonstrated runtime
  mutation defect; they are not required for the ownership ports above.
- [Old-FFmpeg V4L2 filename copy](https://github.com/Genymobile/scrcpy/commit/c29743af4bca0e4a2aa2b30b258099445ed91b16)
  and [follow-up](https://github.com/Genymobile/scrcpy/commit/6ce3d289145bf8acf4eeeaef0ce334f4d448acd7):
  the obsolete `filename` compatibility branch still has potentially
  unterminated `strncpy`. Current FFmpeg selects `url = strdup(...)`, and the
  supported Windows build does not compile V4L2. This is not an established
  active Windows flaw. Old-FFmpeg/Linux V4L2 compatibility remains unvalidated;
  claiming it requires the bounded-copy port and conditional source/runtime
  coverage. Windows usb=false or usb=true results do not exercise that branch.

The Android control-channel specification and independent bidirectional codec
fixtures live under [spec/device-protocol](../../spec/device-protocol/PROTOCOL.md).
They inventory current wire behavior separately from upstream feature adoption,
Desktop/native IPC and untested media/device effects. None of these source
decisions establishes a historical hardware failure or explains the retained
geometry and activation observations.
