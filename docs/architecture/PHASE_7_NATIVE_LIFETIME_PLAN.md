# Phase 7 native application and connection lifetime plan

Status: proposed, pending review and separate implementation authorization.
Source inspection date: 2026-10-02. All current-source statements below refer to
integration `eaccb407cdf823af3ab6d1ab3de1f796cbd4d775`; conceptual APIs and
checkpoints are proposals, not implemented guarantees.

## Authority, integration and evidence boundary

The accepted [target](SEAMLESS_2_TARGET.md), [Phase 0 baseline](SEAMLESS_1_BASELINE.md),
[risk register](SEAMLESS_2_RISK_REGISTER.md), [execution plan](../exec-plans/active/seamless-2.md),
[machine runtime](../development/desktop-native-runtime.md) and
[wire contract](../../spec/desktop-native/PROTOCOL.md) remain authoritative.
The adopted master charter's sections 14–18, native reconnect harness and
Phase 7/8 definitions require application/session separation without a framework,
language, build-system or wholesale directory migration.

Phase 6C engineering integration remains accepted through PR #12. The separately
accepted numeric-zero preflight correction was integrated by
[PR #14](https://github.com/Nexusov/scrcpy-seamless/pull/14), merge
`eaccb407cdf823af3ab6d1ab3de1f796cbd4d775`, ordered parents
`f08e603f0a4187b227c67a1eec4d845b814e095e` and
`10bffce348c587ebf64ec96a639d3870989d615c`, tree
`7e90594e64bbdf397d94398858bceee972ceb0c0`. The
[acceptance decision](https://github.com/Nexusov/scrcpy-seamless/pull/14#issuecomment-5940633310)
and [completed issue #13 resolution](https://github.com/Nexusov/scrcpy-seamless/issues/13#issuecomment-5940669161)
settle that defined source preflight defect, not every deadline/reconnect scenario.

On this inspection, separate post-merge [run 36926881013](https://github.com/Nexusov/scrcpy-seamless/actions/runs/36926881013)
was completed/success, push event, attempt 1. Jobs native `110586289596`,
android-server `110586289787`, test `110586289801` and desktop `110586289863`
all succeeded; each retrieved checkout log identifies the full integration SHA
above. This supersedes the earlier pending-at-the-time observation; it is not a
new local test execution, hardware run or validation of a future Phase 7 runtime.

Historical evidence stays in the [Phase 6C acceptance record](../development/phase6c-manual-acceptance.md#current-artifact-evidence-and-review-gates).
Recovery/activation/close observations on `g3a99a1ed` and USB/Focus/Stop on
`g81fba9b1` stay separate. The latter source remains
`81fba9b17843fae03b18812bb7bae831e13d540a`, ZIP SHA-256
`cc31ca51a770266bbed79d2be1ab69ae1fd8a2ae1acc4f2af3dfcd6bb4805660`;
its preflight bytes do not acquire issue #13's correction. No artifact is rebuilt
or relabelled by this plan. R05/R06/R12/R15 remain open.

## Current source ownership map

Paths in this map are relative to `src/scrcpy/app/src/`. Linked modules and exact
symbols locate implementation evidence; initialization flags in `scrcpy()` also
form part of the current cleanup contract.

| Group and source anchors | Creation, ownership and borrowing today | Threads, cancellation and release today | Proposed lifetime |
| --- | --- | --- | --- |
| [main.c](../../src/scrcpy/app/src/main.c): `main_scrcpy`; [ipc/machine.c](../../src/scrcpy/app/src/ipc/machine.c): `sc_machine_init`, `sc_machine_handshake`, `sc_machine_start`, `sc_machine_destroy` | Main owns parsed arguments/network/SDL. Process-static machine owns pipes, bounded writer queue, copied commands, exact-parent handle and guard; it borrows the main-thread window through `sc_machine_set_window`. Machine guard/handshake precede ADB/media. | Reader, writer and exact-parent watcher are process-scoped. Stop/channel failure/parent death request sticky shutdown; close command admission, settle callbacks, cancel/join I/O, then retire guard and handles. Main emits final lifecycle after `scrcpy` returns; SDL quits through atexit. | Application, independent of connection replacement; keep ordinary CLI and launcher-stop monitor separate from machine activation. |
| [scrcpy.c](../../src/scrcpy/app/src/scrcpy.c): `struct scrcpy`, `scrcpy`, `restart_session`, `event_loop`, `await_for_server`, `sc_wait_reconnect` | Static aggregate owns nearly all attempt and screen storage. Screen initialization survives the restart label; other init/start flags reset per attempt. Options strings remain valid for the whole invocation. | Server wait and fixed retry wait pump SDL. Cleanup requests stop, then performs synchronous joins on SDL main thread. Retry drains selected payloads/events before reusing storage. | One app coordinator with one replaceable generation owner; no permanent parallel old/new authorities. |
| [screen.c](../../src/scrcpy/app/src/screen.c): `sc_screen_init`, `sc_screen_rebind`, `sc_screen_prepare_reconnect`, `sc_screen_update_frame`, `sc_screen_destroy`; [screen.h](../../src/scrcpy/app/src/screen.h): `sc_screen` | Owns SDL window/renderer/texture, displayed `frame`, paused `resume_frame`, mailbox, mutex, title, geometry/orientation, capture, FPS/disconnection helpers and optional event watch. Also embeds input manager and borrowed controller/processors/file-pusher. Decoder/delay sink borrows screen. | Demux/delay producer opens/pushes/closes screen sink; SDL thread consumes/renders. Stop producer, join demuxer (including sink-close cascades), then screen helper joins/destroy. Retry retains window/displayed frame and resets pending/paused state. | Presentation and UI helpers app-owned; session bridge borrows it until joined. Controller/processors must move behind a revocable input binding. |
| [frame_buffer.c](../../src/scrcpy/app/src/frame_buffer.c): `sc_frame_buffer_push`, `sc_frame_buffer_consume`; `sc_screen_clear_pending_frames` | Mailbox refs the incoming AVFrame, swaps pending/temp, moves ref on consume. Screen supplies synchronization. Screen currently reaches directly into pending/temp/consumed fields during reconnect. | Producer publication and main consumption use screen mutex; existing reset relies on producer joins. SDL NEW_FRAME notification itself owns no frame. | App presentation keeps displayed ref; generation mailbox ingress accepts only current token. Reset/drain/transfer become explicit mailbox APIs with stated lock owner. |
| [input_manager.c](../../src/scrcpy/app/src/input_manager.c): `sc_input_manager_init`, `sc_input_manager_handle_event`; [mouse_capture.c](../../src/scrcpy/app/src/mouse_capture.c) | Input manager borrows screen/controller/file-pusher/key/mouse/gamepad processors. SDK/UHID processors borrow controller; AOA processors borrow AOA. Reconnect clears screen controller and gates `im.disconnected`, but does not revoke every embedded borrower. Capture belongs to persistent window. | SDL handles input; control enqueue and file push create async work. Receiver clipboard/UHID work reaches SDL separately. Bind replacement while reconnect gate is set; first valid presented frame releases input gate/restores capture when focused. | App input router owns bindings/capture policy; endpoint and processor storage belong to generation. No remote input replay across revocation. |
| [server.c](../../src/scrcpy/app/src/server.c): `sc_server_init`, `run_server`, `sc_server_stop`, `sc_server_join`, `sc_server_destroy` | Owns serial/socket-name strings, tunnel, intr, server worker/observer and video/audio/control sockets. Actual init shallow-copies params, borrowing const option strings despite broader header wording. Demux/controller borrow sockets. Server currently reads reconnect environment/matches selected serial. | Stop signals cond and interrupts startup operations; server worker shuts down sockets, waits briefly for server process, retires observer. Join server after borrowers; destroy closes sockets/strings/intr/sync. Init failures unwind only acquired resources. | Connection generation owns server and channels; immutable app launch values outlive it. Preserve resolved legacy behavior through explicit adapter before Phase 8 removes policy from server. |
| [controller.c](../../src/scrcpy/app/src/controller.c): `sc_controller_init`, `sc_controller_configure`, `sc_controller_start`, `sc_controller_stop`, `sc_controller_join`, `sc_controller_destroy`; [receiver.c](../../src/scrcpy/app/src/receiver.c) | Controller owns send queue/thread and receiver; both borrow server control socket. Receiver borrows acksync/UHID devices. Clipboard payload transfers string ownership; UHID queued payload owns allocations but borrows device registry. Controller callback can occur from both sender and receiver. | Stop signals sender termination; current enqueue/resize APIs do not reject stopped state independently. Server socket shutdown wakes receive/send; join sender and receiver, then queue/message/sync destruction. Generic main-thread gate drains accepted receiver tasks before release. | Generation owns controller/receiver/processors and ack registry; revocable bindings prevent late enqueue, and queued tasks resolve targets by value identity. Duplicate completion is idempotent. |
| [file_pusher.c](../../src/scrcpy/app/src/file_pusher.c): `sc_file_pusher_init`, `sc_file_pusher_stop`, `sc_file_pusher_join`, `sc_file_pusher_destroy` | Owns copied serial, queued file requests, intr and lazily started worker; borrows configured push-target string. Input manager borrows it. | Stop signals consumer termination/interrupts own subprocess; request API does not independently reject stopped state. Join if started, destroy queued payloads/sync/strings. Do not make shared ADB a session resource. | Generation service endpoint; queued work settles before release, no late enqueue after input revocation. |
| [demuxer.c](../../src/scrcpy/app/src/demuxer.c): `run_demuxer`, `sc_demuxer_start`, `sc_demuxer_join`; [decoder.c](../../src/scrcpy/app/src/decoder.c): `sc_decoder_open`, `sc_decoder_close`; [trait/packet_source.c](../../src/scrcpy/app/src/trait/packet_source.c), [trait/frame_source.c](../../src/scrcpy/app/src/trait/frame_source.c) | Video/audio demuxers own per-stream codec context/packet processing and borrow sockets. Decoder borrows demux codec context, owns decoded AVFrame; source/sink registration borrows downstream objects. Opening sinks can fail partway and unwind already-open sinks. | Demux thread decodes/pushes synchronously. On EOS/error closes packet sinks → decoder closes frame sinks, then frees codec context. Socket interruption precedes demux join; join settles downstream close, not just packet reader. | Generation owns graph and open/start ledger; generation-aware callback destinations and presentation ingress. Preserve sink open/close cascades rather than double-owning resources. |
| [delay_buffer.c](../../src/scrcpy/app/src/delay_buffer.c): `sc_delay_buffer_init`; [audio_player.c](../../src/scrcpy/app/src/audio_player.c): `sc_audio_player_frame_sink_open`, `sc_audio_player_stream_callback`, `sc_audio_player_frame_sink_close`; [audio_regulator.c](../../src/scrcpy/app/src/audio_regulator.c) | Video/V4L2 delay owns queued refs/worker; audio player owns SDL stream (which owns device), callback buffer, regulator/resampler/audio queue. SDL callback userdata borrows player/regulator/buffer; decoder pushes from demux thread while SDL pulls on audio thread. | Delay sink-close stops/joins buffer worker before downstream close. Audio sink-close pauses device, destroys stream, then regulator and callback buffer. Regulator serializes queue access. Explicit callback quiescence must be established before new ownership frees its storage. | Generation owns full audio pipeline, including SDL callback context; app only retains requested settings and SDL subsystem. Android capture is server-side, not owned by native decoder. |
| [recorder.c](../../src/scrcpy/app/src/recorder.c): `sc_recorder_init`, `sc_recorder_start`, `sc_recorder_stop`, `sc_recorder_join`, `sc_recorder_destroy`; [util/timeout.c](../../src/scrcpy/app/src/util/timeout.c) | Recorder owns packet queues/worker/output context; demuxers borrow packet sinks. Timeout owns worker/sync and borrows callback. Current positive deadline starts after media setup; recording/deadline prevent reconnect. | Stop queues/timer, interrupt sockets, join demuxers before recorder destroy; stop/join/destroy timeout. Recorder still finalizes normally. | Keep existing single-generation semantics first; overall deadline intent belongs to app policy in Phase 8, segmented recording to Phase 8. No enabling reconnect here. |
| [events.c](../../src/scrcpy/app/src/events.c): `sc_run_on_main_thread`, `sc_main_thread_stop`, `sc_main_thread_resume`; `scrcpy.c` callback family; `sc_screen_discard_pending_open_window_events` | Process-static mutex/stopped gate; raw callback data, untagged server/demux/controller/timer notifications. OPEN_WINDOW owns size; disconnected icon owns SDL surface; NEW_FRAME is mailbox wakeup. Machine Focus bypasses this session gate via SDL callback. | Stop gate and pump accepted callbacks, join producers, explicitly free size payloads, then flush a user-event range. Final screen destruction releases icon payload. Blanket flush would not establish exactly-once cleanup or preserve app work. | One app dispatcher with scoped envelopes, explicit destructor and optional waiter; lifetime fences replace gate cycling and broad session flush only after all producers migrate. |
| [usb/aoa_hid.c](../../src/scrcpy/app/src/usb/aoa_hid.c), [usb/usb.c](../../src/scrcpy/app/src/usb/usb.c), [usb/scrcpy_otg.c](../../src/scrcpy/app/src/usb/scrcpy_otg.c), [uhid/uhid_output.c](../../src/scrcpy/app/src/uhid/uhid_output.c), [util/acksync.c](../../src/scrcpy/app/src/util/acksync.c); [v4l2_sink.c](../../src/scrcpy/app/src/v4l2_sink.c) | Conditional USB/AOA owns libusb/AOA workers/HID endpoints, shared ACK sync; OTG is separate entry. UHID is control-channel based. V4L2 sink borrows decoder/delay output and owns output device state. | Existing AOA/USB/ACK release occurs before controller join: buffered receiver ACK is a source-derived borrowing risk requiring a controlled test, not a reproduced failure. V4L2 destroy follows demux join. | Preserve guarded builds/OTG entry. Reverse dependency release must include receiver before ACK/UHID storage. Windows pin uses `usb=false`, no V4L2; its green tests do not validate these conditional paths. |

### Inspection conclusions and limits

The static aggregate, untagged notifications, raw UHID borrower and gate cycling
are structural R05 risks; this inspection did not reproduce a UAF. The global
gate holds its mutex across `SDL_RunOnMainThread`, including its synchronous
API option; current receiver callers use `wait_complete=false`. A synchronous
wait/gate/SDL-join deadlock is a latent API risk, not a newly observed incident.
Synchronous joins before responsive retry waiting are an actual code structure
behind R06; worst-case delay is not measured here.

Legacy visible-video/no-recording/no-positive-deadline/no-AOA reconnect restrictions
are intentional compatibility constraints at this base, not new lifetime defects.
The earlier unexplained audio failure remains classification C; packet drops do
not establish its cause. Existing Stop/terminal/cleanup/first-initiator and Focus
permission corrections are accepted boundaries to preserve, not findings to reopen.

## Proposed ownership and identity contracts

`sc_app` owns dispatcher, presentation, input router, immutable launch values,
sticky stop/quit intent and the one current generation slot. `sc_session` owns
one initialized connection graph and its worker/resource ledger. These are small
concrete owners, not a generic service/container framework. Start sequentially:
there is at most one unretired generation; replacement begins after retirement.
No concurrent old/new media graphs are needed for characterized recovery.

| Identity | Meaning and rule |
| --- | --- |
| Protocol `SessionId` | Desktop-acquired native process operation. Stable across reconnect; not a transport generation and not changed by this refactor. |
| Protocol `ConnectionAttemptId` | Actual connection attempt GUID, allocated before each server start; remains wire correlation, not a memory-safety token. |
| Native connection generation | App-owned monotonic value allocated before any generation producer starts; never reused during process lifetime. Check overflow and reject another start rather than wrap. Registry state is app-owned. |
| `sc_stream_session` in [trait/packet_sink.h](../../src/scrcpy/app/src/trait/packet_sink.h) | Device video metadata (width, height, `client_resized`), copied by decoder/screen; stream updates may occur within one connection. It contains no connection token or stream GUID. Not protocol SessionId or native generation. |
| Server `scid` | Random 31-bit server/socket differentiator from `scrcpy_generate_scid`; recreated per attempt, not a stale-callback guard. |
| Child PID/creation/window | Exact process/window observations; remain app/process lifetime identities, not stream readiness or successful resource settlement. |

Worker code may borrow its own generation only while its owner keeps storage
alive through stop/join. Queued work must carry generation **by value** and owned
payload, not a session pointer to dereference for validation. SDL dispatch checks
app-owned slot/token/admission first, then resolves a live endpoint. Execution
borrows it only for the callback, does not retain it or pump a nested lifecycle
transition. If a callback must re-enter dispatch, use an explicit execution lease
and postpone owner destruction until it returns; generation comparison alone is
not a lease. No generic pointer-to-current-session cache survives replacement.

Init ledgers distinguish initialized, started, opened and joined. Each module's
init failure retains its own partial unwind; an owner destroys only successfully
acquired stages. Failed thread creation is not a started worker. Sink graphs
retain existing rollback of partial open, and downstream close has exactly one
owner. Captured session result, cleanup result and initiating stop intent remain
distinct immutable facts.

## Dispatcher, payloads and waiters

The app owns one admission gate and queue for its whole lifetime. SDL wakeups
schedule draining; they do not own a raw session callback. An envelope contains
app or generation scope, captured token, typed operation, owned payload/destructor
and optional separately owned completion object. Existing machine writer and
protocol consumer remain single owners; this is not another IPC reader.
Admission bounds retained envelope count and owned payload bytes with named
limits, tested at exhaustion; no unbounded receiver backlog. Size accounting
includes transferred clipboard/HID allocations, not just envelope structs.
P7.1 must select/document those limits against existing device message bounds,
and reject excess work without transferring ownership. Sticky app stop/quit has
an independent wakeup so a full generation lane cannot starve termination;
machine Focus keeps its existing finite admission limit and attempt semantics.

1. A failed/rejected enqueue leaves payload ownership with the producer.
   Successful admission transfers it once to the dispatcher.
2. Generation revocation and enqueue serialize under a short admission gate.
   Accepted-but-not-executed work is cancelled or drained with its destructor;
   execution revalidates the token before fetching an endpoint. Late posts reject.
3. Execution, stale rejection, caller cancellation and app shutdown each reach
   one terminal completion and exactly-once payload release. Cancellation may
   settle the caller while execution remains active; the execution owner retains
   the payload/endpoint lease until callback return before its destructor runs.
   Waiter departure does
   not free a queued envelope; separate references or an equivalent explicit
   owner keep completion storage valid until both queue and caller release it.
4. Do not invoke callbacks/destructors, wait, join, do SDL/pipe I/O or emit machine
   events while holding admission/registry locks. Main-thread synchronous calls
   execute through a checked inline path or reject; they never wait on themselves.
   Worker synchronous waits terminate on execute/revoke/shutdown without requiring
   a worker holding the same gate to finish.
5. Stop/Focus/quit use app scope and survive generation invalidation. Focus resolves
   the current app window on the SDL thread, retains `Applied` as attempt outcome,
   and preserves the managed exact-child permission handoff. Stop is sticky and
   precludes replacement admission even if late connect/teardown work arrives.
6. At final shutdown close generation admission, join producers, settle queued
   payloads/waiters and app commands. Close/quiesce machine callback publication,
   settle pending Focus work and retire the exact-parent guard and legacy stop
   monitor before freeing any dispatcher/app storage they could reach; preserve
   the guard through graph teardown and existing finite policies. Then destroy
   dispatcher before SDL shutdown.
   A blocked worker does not permit freeing its borrower or completion storage.

Current machine publication lock ordering is `stop_publication_mutex` then
`machine.mutex`. Keep queue/registry locks outside this order; publish observations
after releasing them. Replace the per-generation global stop/resume only after
migrated producers have explicit scopes; compatibility wrappers use the same
dispatcher, not a second lifetime authority. Preserve app events and SDL quit;
remove only owned generation envelopes, not a blanket SDL queue.

## Presentation, frames, input and audio

Presentation owns the last displayed AVFrame ref, texture and persistent window
state. Generation output enters through a revocable, token-carrying sink bridge.
Mailbox API provides discard/reset/consume with explicit locking and AVFrame ref
ownership; no external owner resets private fields. A rejected old frame releases
its ref without replacing the displayed frame. Size/orientation updates and initial
open payloads are generation-scoped too; stale NEW_FRAME wakeups cannot consume a
replacement frame under the old identity.

Preserve current pause behavior at the initial migration: displayed visual frame
survives loss; stale pending/resume refs are cleared and pause resets as today.
Only an accepted current frame updates new geometry/rotation/title and clears the
reconnect gate after actual render/present. Preserve user window dimensions,
fullscreen/render-fit and requested capture restoration; no new pause/recovery
product semantics. A no-window path owns no SDL presentation but must still
settle session work normally; enabling headless recovery belongs to Phase 8.

Input detach executes on SDL main thread before controller stop/destruction.
Revoke the whole binding (controller, key/mouse/gamepad, file-pusher), gate new
input and release capture. In-flight receiver/HID/clipboard operations also
carry generation scope and settle before their registries/ACK sync die. Already
accepted controller messages remain owned by the old queue until drained or
destroyed there; they never transfer to the replacement. UI resize/close/Stop
remains app-scoped; control-related resize is withheld while detached. Rebind
valid endpoints only after initialization, retaining the existing first-presented-
frame gate for visual playback. Reconcile pressed/capture state explicitly;
do not replay queued old input or silently change SDK/UHID/AOA mappings.

Audio generation ownership follows capture/server → socket/demux → codec/decoder
→ regulator/resampler/queue → SDL stream/device/callback. Native does not own
Android's playback source. Close must stop incoming frames, settle sink closure,
exclude running callback access, then release regulator and callback buffer.
Audit the pinned SDL 3.4.8 header/backend contract when implementing: pause alone
is not proof of callback quiescence. Use supported stream/device synchronization
and actual destroy/unbind completion, with a gate proving a callback cannot access
freed userdata. Keep locks off stop/join waits; do not let an SDL callback wait on
the SDL main thread. Preserve partial-open failures (regulator, output allocation,
stream open, resume) without double destroy. This plan establishes ownership,
not audio recovery root cause or audible readiness from a first packet.

## Transitions and serviceable teardown

```text
app Running / no generation
  -> create generation ledger -> Starting -> Active
  -> loss or startup failure -> revoke + detach -> StopRequested -> Joining
  -> Joined -> release generation -> Retired
  -> existing recovery adapter wait -> next Starting, only if app still Running

app Stop/quit/window close from any state
  -> sticky Stopping -> same revocation/retirement, never another generation
  -> app command/payload settlement -> presentation/dispatcher release -> exit
```

For loss, retain presentation and failed-generation result; the compatibility
adapter preserves current retryable outcomes. Normal completion, fatal startup
and partial init unwind use the same acquired-resource ledger without inventing
started workers. A WindowClosed result differs from explicit Stop even though
both retire the same graph. Machine terminal reason selection remains unchanged.

Proposed release dependency order:

1. Revoke generation work and input bindings; cancel its synchronous waiters.
   Record initiating intent before callers join existing cleanup.
2. Request stop on timer, controller/file/record queues, delay workers and optional
   AOA/USB; interrupt server startup and socket I/O. Do not join on SDL thread.
3. A generation-owned retirement worker joins non-SDL producers in reverse
   dependency order. Demux joins include decoder/delay/audio sink closes;
   receiver/controller joins precede destroying UHID/ACK/processor storage.
   No late callback needs SDL execution to complete a join: revoked work settles
   with cancellation, rather than awaiting presentation while main waits for it.
4. Retirement completion posts owned **app-scoped** progress with the retired
   generation value, so invalidation cannot lose the join receipt. Main validates
   the retained retiring owner; no callback dereferences freed session storage.
   Release graph/socket/sync allocations only after every borrower is settled.
   Destruction requiring SDL main thread is scheduled there and acknowledged
   before declaring the ledger retired. The receipt may precede the retirement
   worker's return: retain its handle and owner, observe actual completion and
   join/reap that worker before owner/dispatcher release or `Retired`. Main must
   not block waiting for a still-running retirement thread merely because its
   receipt arrived. Shared ADB is never a resource to stop.
5. Reset generation ingress through APIs, retain or destroy presentation according
   to app intent. Admit next generation only after complete retirement. Final
   `SessionStopped` follows actual resource settlement; process exit is separate.

While a join is held at a test gate, main continues SDL wait/dispatch/render,
move/resize/Focus/Stop/quit. Stop cannot resurrect work; app exit cannot free a
live retirement worker. If a worker never settles, retained ownership stays
honest; existing managed and exact-parent escalation policies still apply.
No unsafe thread termination, raised budgets or false `SessionStopped` makes
this safe. Moving blocking joins off main removes the structural SDL blockage;
it does not guarantee that all native shutdown completes within existing budgets.
Any demonstrated budget conflict requires a separate review, not a Phase 7
timeout increase. Phase 8 still owns policy-level responsiveness under repeated
resolve/connect/backoff and capability changes.

## Dependency-ordered proposed checkpoints

Every checkpoint requires a separately reviewed bounded implementation scope.
Keep current CLI/legacy/machine routes buildable and coherent at each step.
These are semantic changes in current paths; moving `src/scrcpy` is not required.

| Checkpoint / prerequisites | Affected production symbols and ownership | Minimum production-linked tests and observable exit | Coherent intermediate state / deferral |
| --- | --- | --- | --- |
| **P7.1 app dispatcher and receiver work**; reviewed payload/admission contract | `main_scrcpy`, `events.c/.h` gate APIs, receiver clipboard/UHID tasks, `scrcpy` generation begin/revoke boundaries. Main owns dispatcher for process lifetime; receiver work resolves current endpoint by value token and owns payload cleanup. | Actual dispatcher plus receiver adapters: execute/reject/revoke/shutdown exactly-once payload/waiter counts; enqueue-vs-revoke gates; old generation delivered after replacement; partial init; synchronous waiter termination. Existing machine Focus/Stop/parent-death tests remain green. No stale-pointer dereference to validate token. | Retain aggregate, sequential restart and legacy event drain for still-untagged notifications/frames. Machine app commands retain their current path. Do not claim full event-generation safety or responsive joins yet; no new reconnect policy. |
| **P7.2 presentation/input/frame ingress**; P7.1 | `sc_screen_*` sink/reconnect/rebind/update, `sc_input_manager_*`, frame-buffer APIs and source bridges. App owns visual state/router; generation binds one endpoint bundle and revocable frame ingress. | Real screen/frame/router code with controlled frame refs and controller/file/HID boundary doubles: pending/paused/last refs, stale open/frame/size/rotation, detach before release, in-flight input, first-frame gate, capture reset/restoration and partial screen init. Resource destructor counts and existing input behavior. | Same restart orchestration uses APIs, retains PID/window/last visual frame; raw non-media status events still use the old producer join/drain bridge. No shortcut redesign/headless recovery policy. |
| **P7.3 explicit generation graph and complete scoped producer migration**; P7.2 | `struct scrcpy` attempt fields become `sc_session` ledger; server/controller/receiver/demux/decoder/delay/audio/recorder/timer callback data and `sc_push_event` notifications gain captured scope. App owns immutable options/slot; generation owns all graph borrowers. | Invoke actual create/start/request-stop/join/destroy paths with boundary failures at each acquired stage; duplicate controller callback; old server/error/deadline/media event rejected; audio callback held through close; ACK/receiver borrowing order; independent sink-open rollback. Count one close/join/destroy per acquired stage. | Existing orchestration composes owner APIs rather than another state machine. Keep synchronous retirement temporarily and label its responsiveness limit; remove broad generation flush only once every corresponding producer is migrated. Recording/deadline/AOA restrictions remain. |
| **P7.4 asynchronous retirement**; P7.3 complete event/payload ownership | `scrcpy` cleanup, owner stop/join/destroy, SDL loop progress and presentation finalization. Retirement worker owns joins; main owns SDL-only work and retiring owner until receipt. | Hold server/demux/delay/audio/controller/file/recorder completion individually; run actual main pump and verify app events handled before gate release, storage retained, waiters settle, Stop/quit prevents replacement, disconnect during retirement is idempotent; final destruction only after all receipts. Preserve machine finite shutdown/escalation tests. | Existing restart path can wait through a serviceable retirement state then invoke same policy. No parallel replacement graph or resource release on timeout. Phase 8 transport policy responsiveness remains separate. |
| **P7.5 one app coordinator and legacy recovery adapter**; P7.4 | Replace `restart_session`, `event_loop`, `await_for_server`, `sc_wait_reconnect` orchestration with app transition handlers calling the same owner APIs. Extract current target/retry choice; give server explicit equivalent resolved inputs instead of selecting policy from environment. | Actual coordinator with controllable transport/server/clock boundaries: initial failure, active loss, late completion, Stop/quit during startup/wait/retirement; characterize current retry selection/1-second wait/fallback-disabled behavior; no reconnect after stop. CLI list/headless/recording/positive deadline restrictions unchanged. | Delete superseded label/legacy lifecycle bridge in same coherent cutover. Keep legacy host/env input as an adapter at app boundary, not a second authority. No candidates/backoff/budget/hysteresis/failback/degradation/segmentation changes. |
| **P7.6 consolidated parity and exact-artifact gate**; P7.5 | Same production owners/dispatch/bridges, Meson test targets and existing process scripts; no new lifecycle implementation. | 100 deterministic generation replacements with stable live resource/thread/queue counts, all failure/close matrices below; full native/managed/contract/process checks. Build traceable affected native/Desktop DEV package; verify server/library provenance. Separately authorize targeted changed-runtime hardware acceptance. | Old hardware remains historical. Conditional AOA/OTG/V4L2 coverage needs its own supported build evidence. Soak/fault campaigns and diagnostic export remain Phase 12; Phase 7 exit is not Phase 8 policy or release acceptance. |

## Production-linked harness and validation matrix

Add test seams at real module boundaries, not copies extracted from source or a
parallel fake transition model. A production lifecycle owner/coordinator is linked
unchanged into the test target; transport/server/media/control/clock adapters only
replace external work. Test counters observe acquisition/release, never authorize
product cleanup. Gates pause admission, worker start/completion, callback execution,
frame publication and join receipts. Release gates explicitly; finite outer test
timeouts catch hangs, but sleeps/retry-until-green cannot establish ordering.

The existing [Meson target map](../../src/scrcpy/app/meson.build) and
[native build guide](../development/native-build.md) distinguish debug tests from
debugoptimized production builds. `test_machine_child` links production machine
and codec modules with a test-only main; it does **not** run `scrcpy` graph cleanup.
Extend the new lifetime target to link actual owner/orchestration code as it is
extracted, while preserving that distinction. The
[runtime process check](../../scripts/test-ipc-runtime.ps1) still checks linked
fixture and separate production executable bootstrap; the
[bidirectional codec check](../../scripts/test-ipc-contract.ps1) remains independent.

| Scenario / owning checkpoint | Real path and planned assertions |
| --- | --- |
| Startup failures, disconnect before streaming / P7.3, P7.5 | Actual generation create/start, server callback and sink-open rollback; every injected init/start/open failure releases only acquired resources, emits no fictitious stream readiness and follows existing retry/terminal decision. |
| Active loss and teardown loss / P7.3–5 | Actual coordinator consumes scoped server/demux/controller outcomes, revokes once, joins all borrowers; duplicate notifications cannot double-release or admit two replacements. |
| Queued/late callback, input and frame / P7.1–3 | Gate work immediately before revocation and deliver after replacement exists; no old endpoint mutation, no stale ref rendered, correct destructor/waiter settlement, no stale session dereference. Include cancelled enqueue and shutdown races. |
| Delayed worker/SDL service / P7.4 | Hold each production join seam, post real SDL app events and check main handling before release; window resize/redraw/quit, command Focus/Stop remain serviceable without moving SDL operations to join worker. |
| Audio callback and downstream close / P7.3–4 | Gate real callback/close boundary; player userdata/regulator/buffer remain live through pull; no decode push after sink closure; callback exclusion precedes free; exactly one release at every partial-open stage. Real audible output remains separately manual. |
| Presentation and input / P7.2 | Real mailbox ref/move/reset plus screen/router handlers: last visual frame retained, pause reset as characterized, size/rotation changes accepted only current generation, processor/file/ACK/UHID detach and capture conditions maintained. |
| Stop/window close/disposal/terminal races / P7.4–6 | Reuse native `test_machine_exit` and managed `MachineNativeTerminalTests`, `MachineNativePressureTests`, session/view-model evidence tests: explicit versus automatic intent, terminal/completion order, failed Stop vs successful cleanup, first initiator, escalation and exact-child ownership preserved. No missing result manufactured. |
| Ordinary CLI/legacy/machine / every checkpoint | Native `test_cli` including zero spelling parity; actual `ProductionNativeBootstrapTests`; legacy launcher-stop and host suites; machine process parent-death/graceful Stop/Focus plus foreground-permission/cancellation tests. No silent second legacy child on machine failure. |
| Repeated generations / P7.6 | 100 bounded deterministic cycles through production coordinator, failure/start/retire gates and destructor/queue/thread accounting. Compare stable counts after settled checkpoints, not arbitrary wall-clock snapshots; diagnose monotonic growth. This is not a device soak. |

Partition remaining charter harness requirements explicitly:

- **Phase 7:** initial/active/teardown loss; stale work/frame/input; partial init;
  app cancellation/shutdown; safe audio/worker release; window/last-frame and
  aspect/rotation; deterministic repeated generations and resource accounting.
- **Phase 8:** successful/failed transport replacement classification, retryable
  versus fatal policy, Wi-Fi loss, USB return/failback/flapping, fake-clock backoff,
  budgets/hysteresis/degradation, headless recovery, segmented recording and
  overall positive wall-clock deadline. Reuse the Phase 7 production harness seams.
- **Phase 12:** broad sanitizer/fuzz/fault coverage, real-device repeated soak,
  measured performance/handle growth, audio quality/output-state correlation,
  multi-monitor/accessibility/platform hardening and private diagnostic export.
  Useful targeted ASan/UBSan checks may run earlier when tooling supports the
  affected boundary; no suppression or new platform support claim follows.

For a future code checkpoint run affected native debug tests and process/codec
checks first, then the full applicable solution/native/metadata/SpecGen/DocsCheck
gates before a candidate. Keep locked restore/Release build and server evidence
separate from native test totals. Build/package only changed inputs, reusing
unchanged server/libraries after provenance checks. Production-linked synthetic
coverage is required at each owning boundary; changed native bytes need targeted
separately authorized hardware evidence when presentation/input/audio/recovery
contracts change. Do not schedule a full phone campaign for every mechanical move
or transfer the old package results to new native bytes.

## Lifetime observability alongside migration

At each owner change record bounded generation-aware app/session/worker
init/start/stop-request/join-complete/destroy and stale-work rejection, with
captured SessionId/attempt mapping, UTC and monotonic source timestamps and a
local ordered lifecycle record. Emit outside lifetime locks; preserve unknowns
when an observation is unavailable. Critical transitions flush immediately;
aggregate high-frequency packet/frame/sample counters periodically. Include
audio demux/decoder/regulator/SDL callback start/close/quiescence ownership,
without interpreting packets, dropped samples or callbacks as audible readiness.
Assign later audio metric ownership now: demux counts packets received and first
packet per reconnect attempt; decoder counts packets decoded; regulator/buffer
counts samples submitted/dropped, underflow/overflow and applicable queue depth;
decoder and SDL sink record restart/close boundaries. Phase 7 carries generation
lifetime attribution at these owners, Phase 8 adds recovery timing, and Phase 12
exports periodic aggregates and validates privacy/rotation/output-state evidence.
Do not implement all counters/exporting in a dispatcher-only checkpoint.

Keep stdout exclusively the existing framed machine stream. Current v1 enums
do not include native generation or worker stop/join/destroy events. For initial
checkpoints use bounded allowlisted native stderr lifetime records and test
observations, not ad hoc extra stdout frames or a second reader. Exclude endpoints,
serials, command lines, pairing codes, clipboard content and keys. Any later wire
generation/event extension needs separately reviewed protocol/capability/codec
changes; do not smuggle new event names through v1. Phase 8 owns transport timing
and channel-specific first-packet/control-ready semantics; Phase 12 owns bundle
JSONL schema/export, rotation/privacy policy and full aggregate metrics.

## Recommended first authorization and review decisions

Authorize **P7.1 only** first: app-owned dispatcher admission/envelope, captured
generation token, receiver clipboard/UHID migration and production-linked
ownership/waiter tests. Keep current reconnect, graph, presentation and machine
semantics. Exit at green deterministic payload/late-work checks and preserved
machine/legacy boundaries; no whole-session rewrite in this first slice.

Recommendations requiring design review before their owning implementation:

- Accept sequential retirement-before-replacement; it avoids overlapping graphs
  and matches characterized legacy recovery. Overlap is unnecessary Phase 7 scope.
- Accept the explicit payload-transfer and separate waiter-lifetime contract for
  P7.1. Do not begin receiver migration until rejection/revocation/shutdown
  ownership is agreed; new numeric generation alone cannot repair borrowing.
- For P7.3/P7.4 prove pinned SDL callback quiescence and conditional ACK/receiver
  order in production-linked tests before changing release sites. No new audio
  cause or conditional-platform correctness is established by this plan.
- Use a retirement worker plus main-thread SDL finalization, preserving current
  budgets. A demonstrated finite-shutdown conflict needs a separate decision;
  do not free live state or change Stop semantics to finish a checkpoint.
- Keep v1 unchanged initially and generation evidence off framed stdout. Review
  a minimal negotiated wire extension separately if managed correlation later
  genuinely needs worker/generation fields. No exporter/dashboard is implied.

This planning task changes documentation only. Production, tests, protocol/spec,
generated outputs, dependencies and workflows remain unchanged. Existing AGENTS
already states the proposed separation, producer-before-destroy and stale-input
invariants; no path/build/implemented contract changes require an AGENTS edit.
Implementation authorization, plan acceptance and public release acceptance
remain separate decisions.
