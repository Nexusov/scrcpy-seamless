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
