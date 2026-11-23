# Competitive feature matrix

Phase 10 consolidates the existing Phase 5A decisions and checks the remaining
references. Revision links identify source and documentation inspected for
this scope. These records describe independent design decisions.

## Decision vocabulary

- **ADOPT**: implement a feature that fits Seamless's scope.
- **ADAPT**: use the problem or UX insight while designing an independent fit.
- **ALREADY_BETTER**: retain current Seamless behavior with cited evidence.
- **DEFER**: record a potentially useful feature for a later phase/version.
- **REJECT**: explicitly keep the feature out of product scope.

## Feature records

Add one row per meaningful feature and source revision. Use a link to a pinned
source file, release, or documented screen where available. State the current
Seamless behavior with a source or [baseline](../architecture/SEAMLESS_1_BASELINE.md)
reference, then give a decision and rationale. `Unknown` is preferable to an
unsupported claim.

| Feature | Project and inspected revision | UX evidence | Implementation evidence | License constraints | Current Seamless support | Decision | Rationale |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Device-first status and restrained actions | [SimonAKing/scrcpy-gui](https://github.com/SimonAKing/scrcpy-gui/tree/0cc69ff9595720eb3ac2ff93b7625b291b5fee6d) | Device cards and an authorization warning | [Renderer](https://github.com/SimonAKing/scrcpy-gui/blob/0cc69ff9595720eb3ac2ff93b7625b291b5fee6d/src/renderer/AppV2.vue) | MIT at the inspected revision | Typed device, session and channel status | ADAPT | Keep the main action near the device and separate process state from media evidence. |
| Guided USB and wireless setup | [QtScrcpy](https://github.com/barry-ran/QtScrcpy/tree/f90e2259ea6745c30fc242f501a0210771288f8a) | Distinct USB and wireless controls | [Dialog](https://github.com/barry-ran/QtScrcpy/blob/f90e2259ea6745c30fc242f501a0210771288f8a/QtScrcpy/ui/dialog.ui) | Apache-2.0 | Discovery-first flow and explicit pairing versus connection | ADAPT | Distinguish activation steps and keep recovery in the owned native session. |
| Settings categories | [escrcpy](https://github.com/viarotel-org/escrcpy/tree/1e87397da0535b825d5e2e87a53e08fa3896f4a9) | Category navigation and collapsible preferences | [Form](https://github.com/viarotel-org/escrcpy/blob/1e87397da0535b825d5e2e87a53e08fa3896f4a9/desktop/src/components/preference-form/index.vue) | Apache-2.0 at repository level. Private extensions were outside the source review. | Generated option search and detached Apply/Cancel | ADAPT | Preserve discovery and explicit commit for each settings group. |
| External coordinate mapping | [guiscrcpy](https://github.com/srevinsaju/guiscrcpy/tree/9a912f96117d4cdee96d69f6c43dedadf1d8a214) | The archived PySide app offers a key mapper | [Mapper](https://github.com/srevinsaju/guiscrcpy/blob/9a912f96117d4cdee96d69f6c43dedadf1d8a214/guiscrcpy/lib/mapper/mapper.py) uses input listeners and ADB | GPL-3.0 | Semantic native shortcuts act within the focused mirror window | REJECT | External input interception and coordinate injection exceed the scoped shortcut contract. |
| Nearby network transport controls | [GeorgeEnglezos/Scrcpy-GUI](https://github.com/GeorgeEnglezos/Scrcpy-GUI/tree/3e0548f97246b9996f6b6c9c36aefda79309c3f4) | Grouped network settings | [Flutter panel](https://github.com/GeorgeEnglezos/Scrcpy-GUI/blob/3e0548f97246b9996f6b6c9c36aefda79309c3f4/ScrcpyGui/lib/pages/home_panels/network_connection_panel.dart) | AGPL-3.0 | Saved profiles, pairing and connection observations, and separate media/lifecycle evidence | ALREADY_BETTER | Retain the existing device-first flow for the agreed transport scenarios. This is a scope decision. |
| Companion-app notifications and calls | [ADB Device Manager 2](https://github.com/Shrey113/Adb-Device-Manager-2/tree/88409d07b0921444d8e45366935c7ca108ef995b) | The [README](https://github.com/Shrey113/Adb-Device-Manager-2/blob/88409d07b0921444d8e45366935c7ca108ef995b/README.md) describes ADB and companion-app modes | App implementation unverified in the inspected repository | Apache-2.0 repository notice only | Device discovery, mirroring and ADB operations | DEFER | Companion permissions and data handling require a separate product decision. |
| Onboarding and global shortcuts | [kil0bit-kb/scrcpy-gui](https://github.com/kil0bit-kb/scrcpy-gui/tree/7fbeee9224288739f2cdb01bad8f5cf852c257a1) | [Onboarding](https://github.com/kil0bit-kb/scrcpy-gui/blob/7fbeee9224288739f2cdb01bad8f5cf852c257a1/src/components/OnboardingModal.tsx) guides upstream setup | [Tauri plugin](https://github.com/kil0bit-kb/scrcpy-gui/blob/7fbeee9224288739f2cdb01bad8f5cf852c257a1/src-tauri/src/shortcuts.rs) registers OS shortcuts | MIT at the inspected revision | Self-contained adjacent runtime, guided discovery and focused keyboard actions | ALREADY_BETTER for the packaged setup, REJECT for global shortcuts | Retain the owned runtime and focus boundaries. |

The [Phase 5A audit](UI_REFERENCE_AUDIT.md#phase-5a-evidence-and-decisions)
contains the original design rationale. These additional references introduce
no further ADOPT or ADAPT decisions for 2.0. The review used public source and
documentation only. All shipped implementation and assets keep their existing
provenance.

Preserve guided setup, USB/Wi-Fi/combined mode, automatic failover, native
process/window continuity, retained last frame, explicit reconnect state, and
video/audio/control recovery as [target requirements](../architecture/SEAMLESS_2_TARGET.md).
Do not implement the union of competitor checkboxes or expand into a generic
Android device-management product. Copying code or assets requires the separate
[reuse review](THIRD_PARTY_REUSE.md).
