# Desktop shortcut reference

Settings → Shortcuts separates two focus scopes. Control-center shortcuts use
the existing `DesktopCommandIds` bindings and the `desktop-preferences.json`
Apply/Cancel editor under Appearance & shortcuts. The new tab links to that
editor; it neither copies its command list nor owns another preference store.
The mirror-window list is static, read-only presentation data. Opening it does
not create a native process, query ADB, infer a live key mapping or send input.
It remains available in the in-memory preview.

The reference was checked at source revision
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
Its single maintained presentation dataset is
[`NativeShortcutReference.cs`](../../src/desktop/ScrcpySeamless.Desktop/Presentation/NativeShortcutReference.cs).
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

Arbitrary native per-action rebinding remains deferred to the Phase 7 input
architecture and Phase 10 product integration. This reference adds no global
keyboard hook, ADB key injection, Desktop proxy binding or IPC input protocol.
