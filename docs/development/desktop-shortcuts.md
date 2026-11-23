# Desktop shortcuts

Settings → Shortcuts separates two focus scopes. Control-center shortcuts use
the existing `DesktopCommandIds` bindings and the `desktop-preferences.json`
Apply/Cancel editor under Appearance & shortcuts. The new tab links to that
editor; it neither copies its command list nor owns another preference store.
The native keyboard editor changes only a detached mirroring draft. Configuration
Apply commits it for the next Mirror launch; Cancel restores the committed baseline.
Opening the page creates no process, queries no ADB and sends no input. Mouse,
gesture and file-drop reference rows remain read-only. Preview keeps edits in memory.

## Historical reference baseline

The original read-only reference was checked at source revision
`f57a6d94275a99f6e57a4ff52ccf5d66fffb54c6` against the bundled scrcpy
4.0 fork's [`doc/shortcuts.md`](../../src/scrcpy/doc/shortcuts.md),
[`app/src/cli.c`](../../src/scrcpy/app/src/cli.c),
[`app/src/input_manager.c`](../../src/scrcpy/app/src/input_manager.c),
[`app/src/options.c`](../../src/scrcpy/app/src/options.c) and
[`app/src/shortcut_mod.h`](../../src/scrcpy/app/src/shortcut_mod.h). The input
handler's last source-changing commit is
`6362fa645aae46ab352e8ea08006372807a9bd66`. This is a paraphrased UI
reference to the upstream-derived Apache-2.0 scrcpy implementation; preserve
the repository's upstream notices and [`src/scrcpy/LICENSE`](../../src/scrcpy/LICENSE).
That historical presentation dataset was
[`NativeShortcutReference.cs`](../../src/desktop/ScrcpySeamless.Desktop/Presentation/NativeShortcutReference.cs).
Keyboard identity/default/condition data now comes from the canonical generated
catalog below; the remaining pointer/reference data stays in that class.
It covers the native help's keyboard actions and the relevant mouse,
gesture, file-drop, HID and camera actions; it is not an execution or binding
authority. When changing the native input handler, review this dataset and its
anchored tests.

`MOD` means either Left Alt or Left Windows by default on Windows. The existing
generated `shortcut-mod` mirroring option can override the modifier on the
next launch; a saved value is not proof of the currently running process's
mapping. The Settings link opens that option's existing Control category.
Mirror shortcuts require focus in the native mirror window. Device actions
depend on a connected, controllable session. Android clipboard actions require
Android 7 or newer; `legacy-paste` alters `MOD+V`. Mouse alternatives assume
default SDK-mouse bindings and may change with mouse mode or `mouse-bind`.
Camera and HID shortcuts are identified as conditional. Pressing `MOD+N`
twice means hold MOD while pressing and releasing N twice, not a simultaneous
three-key chord.

The native CLI help omits the second `MOD+N` step, while `input_manager.c`
implements it. The native Markdown reference has the action/gesture columns
reversed for the two file-drop rows; the handler and CLI help determine their
meaning. The UI states the verified behavior and does not reproduce those
errors. It also avoids the broad documentation claim that every Ctrl+key is
forwarded, because modifier overrides and input mode qualify that behavior.

Native per-action editing was deferred at that baseline. The P10 implementation
below adds focused semantic remapping without global keyboard hooks, ADB key
injection, Desktop proxy bindings or a new IPC input protocol.

## P10 implemented remapping contract

The authorized implementation uses one canonical action/default catalog under
`spec/options/native-shortcuts.json`, projected by SpecGen into native and Core
definitions. It covers the 34 existing semantic keyboard actions; pointer
gestures, file-drop and mouse-mode behavior remain their existing input paths.
The catalog identifies actions and contexts; the native input manager retains
the actual video, control, pause, camera, HID and presentation-readiness gates.

One `shortcut-bindings` mirroring option stores a bounded JSON object of action
ID to gesture. Absence inherits all default aliases; an empty gesture disables
that action. A configured gesture replaces its aliases. Custom gestures use
the existing logical `MOD`, optional Shift and one supported key; function keys
may also be unmodified. The existing `shortcut-mod` option selects physical MOD
keys. Default notification/quick-settings first/second-press behavior remains
when inherited; an overridden quick-settings action can use a direct chord.
The parser rejects unknown actions, duplicate entries, unsupported syntax and
overlapping effective bindings. Camera and display contexts may share a chord.
The maximum is 34 overrides and 4096 UTF-8 bytes, with gestures at most 32 bytes.

The dedicated Settings editor operates on the existing detached mirroring
draft, validation, Apply/Cancel, revision and immutable launch-snapshot path.
It does not add a preference store or modify a running child's map. The generic
mirroring editor routes this typed option to Shortcuts instead of exposing JSON.
The focused native input manager consumes the captured immutable map; there is
no global hook, Desktop input proxy, ADB injection or new input protocol.

Key-down ownership records the semantic action and connection generation for
the physical key. A matching release uses that captured action even after MOD
is released. Detach clears generation borrowing and retains only bounded
value-only consumed-key tombstones until release, without replay into a replacement; a press
withheld by the input gate cannot acquire a release in the new generation.
Held device-key releases remain possible for the same live generation if a
presentation pause starts between the press and release. Custom shortcut text
and key events must not leak into Android. Tests use actual input-manager SDL
events and controlled effects, including aliases, repeats, second presses,
conditions, modifier-first release, detach/rebind and text suppression.

The concrete contract was recorded before production edits. SpecGen emits
`shortcut_actions.generated.inc` and `GeneratedNativeShortcutCatalog.g.cs` from
`spec/options/native-shortcuts.json`; hand-maintained keyboard catalogs are not
execution authorities. `NativeShortcutEditor` uses the existing option draft and
Core semantic validator. Invalid edits block Apply; malformed stored JSON is not
silently rewritten and requires Cancel/reload or an explicit reset.

Native DOWN captures only values. A current admitted paired key can send UP when
MOD has already been released or presentation paused. Focus loss settles current
paired keys once and retains consumed-key tombstones; replacement detaches the
lease and cannot receive old UP/repeat. Array bounds cover SDL physical keys plus
the bounded unknown-scancode aliases. The parser uses a 64-KiB temporary arena,
at most 34 action entries and at most two inherited aliases per action.

The current host requires manifest `ShortcutBindingsVersion=1` as well as the
existing connection-plan version. This is a native invocation compatibility
claim, not a wire/capability change or signature. Hash-valid historical bundles
remain read-only archive-verifiable, but older hosts/native are not upgraded by
metadata or by changing stored settings.

Production-linked native tests exercise all 34 action remaps and actual SDL input
handlers with controlled effects, including modifier-first release, input gate,
replacement, focus-loss, aliases, repeated keys, text, HID and camera contexts.
An isolated compatible old-input implementation experiment reproduced a missing
HOME UP after MOD-first release; it is not a pristine historical full build or a
hardware result. The unchanged input-binding neighbor initially exposed an
incorrect text suppression introduced during implementation; the retained red
and corrected green are separate from the old-input experiment.

Headless Desktop tests exercise detached edits, invalid/conflicting values,
explicit malformed-map reset, the existing configuration Apply/Cancel path and
accessible reference scrolling. They do not establish Windows keyboard delivery,
physical media/input or screen-reader behavior. Frozen packages and previous
hardware results remain tied to their original bytes.
