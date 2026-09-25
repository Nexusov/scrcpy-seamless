# Competitive UI reference audit

Status: focused Phase 5A UX review completed on 2026-09-25. This review covers
only the interaction patterns needed for the first Desktop UI foundation. The
broader competitive feature audit remains in Phase 10. An upstream project
name or repository URL is not reuse authorization.

## Candidate sources

| Project | Repository | Inspected commit/tag | License at inspected revision | Audit state |
| --- | --- | --- | --- | --- |
| scrcpy-gui (SimonAKing) | [source](https://github.com/SimonAKing/scrcpy-gui) | [`0cc69ff9595720eb3ac2ff93b7625b291b5fee6d`](https://github.com/SimonAKing/scrcpy-gui/tree/0cc69ff9595720eb3ac2ff93b7625b291b5fee6d) | [MIT](https://github.com/SimonAKing/scrcpy-gui/blob/0cc69ff9595720eb3ac2ff93b7625b291b5fee6d/LICENSE) for this revision; older tagged releases through v2.4.1 were GPL-3.0-only | Focused 5A review |
| QtScrcpy | [source](https://github.com/barry-ran/QtScrcpy) | [`f90e2259ea6745c30fc242f501a0210771288f8a`](https://github.com/barry-ran/QtScrcpy/tree/f90e2259ea6745c30fc242f501a0210771288f8a) | [Apache-2.0](https://github.com/barry-ran/QtScrcpy/blob/f90e2259ea6745c30fc242f501a0210771288f8a/LICENSE) at repository level | Focused 5A review |
| escrcpy | [source](https://github.com/viarotel-org/escrcpy) | [`1e87397da0535b825d5e2e87a53e08fa3896f4a9`](https://github.com/viarotel-org/escrcpy/tree/1e87397da0535b825d5e2e87a53e08fa3896f4a9) | [Apache-2.0](https://github.com/viarotel-org/escrcpy/blob/1e87397da0535b825d5e2e87a53e08fa3896f4a9/LICENSE) at repository level; some advertised advanced features use private EscrcpyX | Focused 5A review |
| guiscrcpy | [source](https://github.com/srevinsaju/guiscrcpy) | Pending | Unverified | Not started |
| Scrcpy-GUI (GeorgeEnglezos) | [source](https://github.com/GeorgeEnglezos/Scrcpy-GUI) | Pending | Unverified | Not started |
| Adb-Device-Manager-2 | [source](https://github.com/Shrey113/Adb-Device-Manager-2) | Pending | Unverified | Not started |
| scrcpy-gui (kil0bit-kb) | [source](https://github.com/kil0bit-kb/scrcpy-gui) | Pending | Unverified | Not started |

Add other relevant maintained frontends only with a dated, pinned source and
an explanation of relevance. Do not infer current maintenance from this list.

## Audit method

For each inspected project, record the inspection date, exact commit SHA or
immutable tag, relevant paths/screenshots or documentation, file-level license
evidence, and any uncertainty. Evaluate setup and connection flow, transport
selection, mirroring controls, settings, accessibility, localization, error
recovery, and visual organization where the source supports an observation.

Keep observations separate from product decisions. The focused Phase 5A
interaction decisions are recorded below; enter broader feature-completion
decisions in the [Phase 10 competitive feature matrix](COMPETITIVE_FEATURE_MATRIX.md).
Before copying any code or asset, apply the
[third-party reuse policy](THIRD_PARTY_REUSE.md).

## Phase 5A evidence and decisions

The following observations come from source and documentation at the revisions
above, inspected on 2026-09-25. They are interaction references, not claims
that a feature works on every device or that its underlying implementation fits
Seamless. No upstream code, images, icons, fonts, or branding were imported.

| User problem and observed approach | Evidence at inspected revision | Seamless adaptation for 5A | Limitation |
| --- | --- | --- | --- |
| Users need to identify a device and why it is unavailable. Scrcpy GUI uses named device cards, serials, readable status, and an inline unauthorized warning; escrcpy uses a device table with status text, an authorization hint, and contextual actions. | Scrcpy GUI [`src/renderer/AppV2.vue`](https://github.com/SimonAKing/scrcpy-gui/blob/0cc69ff9595720eb3ac2ff93b7625b291b5fee6d/src/renderer/AppV2.vue); escrcpy [`desktop/src/views/device/index.vue`](https://github.com/viarotel-org/escrcpy/blob/1e87397da0535b825d5e2e87a53e08fa3896f4a9/desktop/src/views/device/index.vue) | **ADAPT:** a device-first workspace with stable identity, text status, and actionable empty, unauthorized, offline, and failure states. Show availability, selected transport, process state, observed stream state, and unverified channels separately. | Upstream device status and a launch/process indicator do not establish video, audio, or control readiness. The 5A states are explicitly synthetic. |
| Users need a discoverable route from USB to wireless. QtScrcpy gives USB and Wireless their own controls and documents the USB-to-wireless sequence; Scrcpy GUI exposes wireless connect/pair beside its device workspace; escrcpy keeps a compact wireless group with address, pairing code, and discovery actions. | QtScrcpy [`README.md`](https://github.com/barry-ran/QtScrcpy/blob/f90e2259ea6745c30fc242f501a0210771288f8a/README.md), [`QtScrcpy/ui/dialog.ui`](https://github.com/barry-ran/QtScrcpy/blob/f90e2259ea6745c30fc242f501a0210771288f8a/QtScrcpy/ui/dialog.ui); Scrcpy GUI [`src/renderer/AppV2.vue`](https://github.com/SimonAKing/scrcpy-gui/blob/0cc69ff9595720eb3ac2ff93b7625b291b5fee6d/src/renderer/AppV2.vue); escrcpy [`desktop/src/views/device/components/wireless-group/index.vue`](https://github.com/viarotel-org/escrcpy/blob/1e87397da0535b825d5e2e87a53e08fa3896f4a9/desktop/src/views/device/components/wireless-group/index.vue) | **ADAPT:** clearly present USB, wireless, and prepared fallback as distinct transport states, with future setup actions located near the device. In 5A those actions must be marked unavailable or preview-only. | QtScrcpy's documented wireless path requires manual USB/IP/adbd steps; none of these references proves automatic failover. Seamless's failover remains its own requirement. |
| Primary controls become hard to find in a long option form. Scrcpy GUI splits Settings into General, Video, Controls, Recording, Window, and Advanced; QtScrcpy separates Start Config from Advanced Display and Advanced Config; escrcpy groups preferences in collapsible sections with category navigation. | Scrcpy GUI [`src/renderer/AppV2.vue`](https://github.com/SimonAKing/scrcpy-gui/blob/0cc69ff9595720eb3ac2ff93b7625b291b5fee6d/src/renderer/AppV2.vue); QtScrcpy [`QtScrcpy/ui/dialog.cpp`](https://github.com/barry-ran/QtScrcpy/blob/f90e2259ea6745c30fc242f501a0210771288f8a/QtScrcpy/ui/dialog.cpp); escrcpy [`desktop/src/components/preference-form/index.vue`](https://github.com/viarotel-org/escrcpy/blob/1e87397da0535b825d5e2e87a53e08fa3896f4a9/desktop/src/components/preference-form/index.vue) | **ADAPT:** category navigation and search over existing generated option descriptors, with clear defaults, overrides, descriptions, and validation. Keep common settings easy to scan and advanced options discoverable. | These projects have different option schemas. Escrcpy's preference view saves watched changes automatically in [`desktop/src/views/preference/index.vue`](https://github.com/viarotel-org/escrcpy/blob/1e87397da0535b825d5e2e87a53e08fa3896f4a9/desktop/src/views/preference/index.vue); Seamless 5A instead uses an isolated draft, and 5B must define explicit Apply/Cancel semantics. |
| Users need to understand whether a launch failed and what to do next. Scrcpy GUI has a separate Sessions view with state, process ID, launch command, and error; its device workspace has an inline warning. Escrcpy exposes context actions such as reconnecting an offline wireless device, while QtScrcpy has separate connect and disconnect controls. | Scrcpy GUI [`src/renderer/AppV2.vue`](https://github.com/SimonAKing/scrcpy-gui/blob/0cc69ff9595720eb3ac2ff93b7625b291b5fee6d/src/renderer/AppV2.vue); escrcpy [`desktop/src/views/device/index.vue`](https://github.com/viarotel-org/escrcpy/blob/1e87397da0535b825d5e2e87a53e08fa3896f4a9/desktop/src/views/device/index.vue); QtScrcpy [`QtScrcpy/ui/dialog.ui`](https://github.com/barry-ran/QtScrcpy/blob/f90e2259ea6745c30fc242f501a0210771288f8a/QtScrcpy/ui/dialog.ui) | **ADAPT:** explicit session/reconnect presentation and a nearby explanation/action for failure. 5A previews these states without executing reconnect; later integration must use the native runtime's reconnect behavior. | Scrcpy GUI's session state is process-centered. The 5A preview cannot claim hardware-observed recovery or channel readiness. |
| Frequent actions should remain near the current device, while secondary tools should stay out of the primary path. Escrcpy puts per-device actions in a row and additional controls in an expansion; Scrcpy GUI places launch and refresh by the device list. | escrcpy [`desktop/src/views/device/index.vue`](https://github.com/viarotel-org/escrcpy/blob/1e87397da0535b825d5e2e87a53e08fa3896f4a9/desktop/src/views/device/index.vue), [`desktop/src/components/quick-bar/index.vue`](https://github.com/viarotel-org/escrcpy/blob/1e87397da0535b825d5e2e87a53e08fa3896f4a9/desktop/src/components/quick-bar/index.vue); Scrcpy GUI [`src/renderer/AppV2.vue`](https://github.com/SimonAKing/scrcpy-gui/blob/0cc69ff9595720eb3ac2ff93b7625b291b5fee6d/src/renderer/AppV2.vue) | **ADAPT:** restrained card actions and clear visual priority for the current device and connection. | **REJECT for 5A:** broad batch tools, icon-only quick bars, automation, embedded mirror controls, and the union of upstream features. They would obscure the first connection workflow and exceed this slice. |

## Reuse boundary

This review uses the references as inspiration only. Scrcpy GUI's current-tree
MIT declaration differs from its GPL-3.0-only releases through v2.4.1, as
documented in its [relicensing record](https://github.com/SimonAKing/scrcpy-gui/blob/0cc69ff9595720eb3ac2ff93b7625b291b5fee6d/docs/RELICENSING_V2.4.2.md).
The QtScrcpy and escrcpy repository licenses are Apache-2.0; individual
bundled fonts, icons, screenshots, dependencies, and other assets still need
file-level review. Escrcpy's [README](https://github.com/viarotel-org/escrcpy/blob/1e87397da0535b825d5e2e87a53e08fa3896f4a9/README.md)
states that some advanced features come from private, paid EscrcpyX. No
private component or unreviewed asset is eligible for import. Any later code
or asset reuse requires the separate provenance and notice process in
[THIRD_PARTY_REUSE.md](THIRD_PARTY_REUSE.md).
