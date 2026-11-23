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

## Physical keyboard layout and AltGr ports � 2026-11-23

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
