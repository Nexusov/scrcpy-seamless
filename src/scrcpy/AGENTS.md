# Native scrcpy-derived tree agent guide

Phase 6B links vendored MIT yyjson and the IPC codecs into the native client.
Only explicit `--seamless-machine` activation uses framed binary stdio;
ordinary CLI and the accepted legacy Desktop host remain separate. Native
machine-mode ownership and event boundaries are documented in
../../docs/development/desktop-native-runtime.md; exact wire rules live in
../../spec/desktop-native/PROTOCOL.md.

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

P7.1 receiver clipboard/UHID work uses `app/src/dispatcher.h`, owned by `main`
after SDL initialization. Begin/bind/revoke and draining are main-thread-only
and reject callback reentry. Admission transfers an owned payload only on
`SC_DISPATCHER_ACCEPTED`; rejection keeps the producer owner. Capture generation
values, never session-target pointers in queued payloads. Revoke before release,
join producers, release completion callers, then remove exact-instance wakeups
and destroy. Quiesce all threads using a completion handle before releasing its
sole caller reference; cancellation does not release executing payload storage.
Machine Stop/Focus remains on its application-scoped SDL path.
P7.2 presentation uses `app/src/video_ingress.h` and a generation-owned sink
bridge. The single dispatcher binding is `sc_generation_targets` for both
presentation and receiver UHID resolution. Input borrowing lives only in
`sc_input_binding`; detach before destination stop. Visual input requires a
new current frame successfully presented. Move replaced video refs under the
ingress mutex, then unref outside it; buffer release callbacks may reenter.
Revocation does not settle failure-thread destructors: join controller/receiver
posters and video/delay producers before ingress destruction. Remove the SDL
watch before releasing its context; foreign-thread watches resolve no context.
See the lifetime plan's P7.2 contract for independent video reference bounds.
Buffered bridge frame/metadata delivery belongs to the single delay worker FIFO;
ASAP skips timing delay, never preceding metadata. Open precedes worker start;
join precedes downstream close and queue release. The upstream owner must still
serialize publication and close. Preserve real delay-to-ingress composition tests.
See `../../docs/architecture/PHASE_7_NATIVE_LIFETIME_PLAN.md#p71-authorized-implementation-contract`
for limits, waiters, wakeup failure and remaining legacy/ACK containment. Native
debug targets link production dispatcher and receiver adapters with controlled
external-effect doubles; they must not read/write a real clipboard or use ADB.

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

`spec/options/options.yaml` owns native static CLI declarations through
`app/src/cli_options.generated.inc`. Do not edit the generated include. Change
the spec, run SpecGen `generate` and `verify` using the pinned .NET SDK as
documented in `docs/development/options.md`, then run native CLI tests. Keep
the parser and runtime semantic handlers in `app/src/cli.c` hand-written.

A post-2.0 Meson-vs-CMake review is planned separately.

## Validation

Use the build/test commands documented by the current Phase and `/docs/BUILD.md`.

Do not claim physical USB/Wi-Fi recovery, audio recovery, or device-specific
behavior is validated unless a real device test actually exercised it.

When this tree is split into future `src/native/` and `src/server/` locations,
move/split these instructions with the code and remove this obsolete file.
