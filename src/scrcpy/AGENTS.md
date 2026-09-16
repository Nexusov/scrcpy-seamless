# Native scrcpy-derived tree agent guide

Scope: `src/scrcpy/`.

This tree currently contains the scrcpy-derived desktop client and Android
server baseline used by Seamless 1.x. Preserve upstream copyright and
Apache-2.0 attribution.

Read:

- `/AGENTS.md`
- `/docs/CHANGES.md`
- `/docs/BUILD.md`
- `/docs/PACKAGING.md`
- `/docs/development/debugging.md`
- upstream scrcpy developer/build documentation when relevant.

## Current fork-specific concerns

The current Seamless 1.x native fork adds:

- USB-to-Wi-Fi reconnection orchestration;
- persistent window/last-frame behavior;
- controller/input rebinding;
- reconnect-specific server/address handling;
- graceful launcher-stop integration.

These behaviors are characterization requirements during the 2.0 rewrite even
when their implementation is replaced.

## Native engineering invariants

- Application lifetime and connection-session lifetime are distinct concepts in
  the 2.0 target.
- A session must not retain access to resources after its lifetime ends.
- Producers must stop and be joined before resources they may access are
  destroyed.
- Old-session callbacks must never mutate a replacement session.
- Input must never target a stale controller.
- Persistent presentation/window state must not own session controller state.
- Reconnect waiting must not block the main SDL event loop.
- Frame-buffer internals should be accessed through explicit APIs, not leaked
  across module boundaries.
- Avoid holding locks across blocking I/O or callbacks unless the ownership
  model explicitly requires it and is documented.

Local `goto cleanup` patterns are acceptable C. A cross-session restart label
is not the desired long-term lifecycle architecture.

## Debugging native failures

Follow `/docs/development/debugging.md`.

For lifetime/concurrency bugs explicitly inspect:

- owner;
- producing thread/task;
- consuming thread/task;
- cancellation owner;
- stop/join/destroy order;
- session/generation identity;
- queued stale work;
- handle/socket/FFmpeg/SDL lifetime.

Do not "fix" races with arbitrary sleeps.

Use deterministic synchronization and sanitizers where the current Phase makes
them available.

## Upstream ports

Do not wholesale-merge upstream architecture into Seamless merely for
convenience.

For later upstream ports record:

- upstream release and commit/PR;
- reason for the port;
- affected Seamless modules;
- attribution/license requirements;
- tests proving the port.

Critical correctness/security fixes may be ported earlier when the active Phase
allows it.

## Build system

Seamless 2.0 keeps Meson/Ninja for the native tree. Do not migrate to CMake as
part of the 2.0 architecture rewrite unless a concrete blocker prevents 2.0
from shipping and the owner approves the change.

A post-2.0 Meson-vs-CMake review is planned separately.

## Validation

Use the build/test commands documented by the current Phase and `/docs/BUILD.md`.

Do not claim physical USB/Wi-Fi recovery, audio recovery, or device-specific
behavior is validated unless a real device test actually exercised it.

When this tree is split into future `src/native/` and `src/server/` locations,
move/split these instructions with the code and remove this obsolete file.
