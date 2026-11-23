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
