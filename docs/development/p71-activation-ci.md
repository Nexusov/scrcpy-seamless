# P7.1 activation CI gate

## Original execution and retained evidence

[PR #15](https://github.com/Nexusov/scrcpy-seamless/pull/15) remains unaccepted
and unmerged. Original run
[36950070637](https://github.com/Nexusov/scrcpy-seamless/actions/runs/36950070637),
`pull_request`, attempt 1, tested head
`ba560a5bdfe90dfa4675827b270593165ebc96f1` against base
`eaccb407cdf823af3ab6d1ab3de1f796cbd4d775` through synthetic checkout
`88ca8e45778eafede2a55ddb463709a1b20c7527`, tree
`afbcf63216656b8c92032d23571f11d10c7355b2`.

Test, Android server and native jobs succeeded. Native job `110660837316`
actually executed `test_dispatcher` and `test_receiver_dispatch` within 20/20
targets, plus nine canonical vectors/24 bidirectional frames, eight separate
process tests and 29/29 native-backed legacy suites. These are distinct lanes;
this is neither a complete media-graph nor a hardware run.

Desktop job `110660837381` restored locked dependencies, verified SpecGen and
compiled Release with zero warnings/errors, then failed: 554 total, 553 passed,
one failed, zero skipped. `RejectsUntrustedCommandBeforeDeliveringValidSessionFocus`
raised `IOException: Pipe is broken` in helper `WriteAsync` at line 173, called
from line 94 for the first in-size empty-GUID command. Later oversized and valid
Focus paths were not reached. Elapsed duration does not establish a timeout.

Complete original logs, job/run metadata and diagnostic outcomes remain under
ignored `work/phase7/p71/activation-ci/`. Byte-preserving log captures have
SHA-256:

- Desktop: `c36a9c6ef7adfa2111493bba1b6286e7d41b2e840584a61f9764ef8a31cc9a18`.
- Native: `023e1fd4649566d7141130b7b6d4860840d3cf3bd873d60a04f0befb9789fb60`.

Metadata and red input hashes are retained separately. Raw logs, synthetic pipe
names, commands, configuration and binaries are not published. The initial PR
pending-at-the-time observation and this original failed attempt remain visible.

## Demonstrated fixture boundary and unresolved historical cause

The [service](../../src/desktop/ScrcpySeamless.Infrastructure/Activation/WindowsControlCenterActivation.cs)
and [test](../../tests/desktop/ScrcpySeamless.Infrastructure.Tests/Activation/WindowsControlCenterActivationTests.cs)
were byte-identical at base and reviewed head. One frozen focused run passed
1/1; the frozen class passed 5/5. Neither result dismisses the hosted failure.
There is no test serialization override; MTP/xUnit use normal concurrency.

[Controlled production-linked tests](../../tests/desktop/ScrcpySeamless.Infrastructure.Tests/Activation/ActivationPeerSchedulingTests.cs)
start the actual helper before a unique synthetic server exists, establishing
an incomplete connection, and hold only its captured caller context. The real
listener accepts and arms its unchanged five-second request budget. Red observes
read start with no received bytes, settled request-deadline cancellation, exact
pipe disposal, then the released helper's first write raises `IOException`
(`0x800700E8`). The failed invariant is that fixture peer publication depends on
the caller's continuation queue after transport acceptance.

The helper correction adds `ConfigureAwait(false)` to connect/write/flush/read
and links test-owned cancellation without extending its ten-second budget.
Green completes the actual invalid exchange with `NO` while the caller context
remains held. A separate deliberately silent peer verifies genuine expiration
and post-disposal write failure. Both cases then collect exactly one fresh
`FocusSession` with its requested SessionId from the same primary, complete the
channel, and reacquire ownership. The original malformed/oversized sequence now
also verifies terminal stream exhaustion after its valid Focus; its oversized-
only IOException allowance is unchanged.

This demonstrates and corrects a fixture context dependency. It does **not**
prove that the original CI failure used that mechanism, that its deadline fired,
or that ThreadPool starvation occurred. The local ordinary runner's caller
context was `None`; the original job has no corresponding server-stage trace.
`ConfigureAwait(false)` does not guarantee progress under arbitrary scheduler
starvation. No shipping activation defect or historical native regression has
been established. A later green run alone cannot reconstruct the original cause.

## Observation seam and validation boundary

The optional internal
[observer](../../src/desktop/ScrcpySeamless.Infrastructure/Activation/ActivationObservation.cs)
records monotonic observation ticks, acceptance/read/response/disposal stages,
byte counts, typed cancellation and HResult only. It must not block or throw.
Normal public composition installs no observer. No pipe security, parser,
ownership, channel limit, public API, deadline or shipping error policy changes.
Cancellation timestamps identify settled cancellation, not exact timer firing;
`ReadProgress` counts one read and `LineComplete` includes the newline. Tests
retain at most 256 output entries and settle their peer, posted callbacks,
collector/enumerator and listener before releasing owned resources. No real
application root, clipboard, HID, ADB or device is involved.

The expected red is retained; new controlled cases passed 2/2, and a separate
focused trace captured the real green exchange. A compile failure requiring
explicit xUnit cancellation-token arguments is retained; those call sites were
corrected, without analyzer suppression. Final activation classes passed 7/7
and Infrastructure passed 201/201. Locked restore, SpecGen (113 entries/six
outputs) and Release build (zero warnings/errors) passed. The full solution with
normal concurrency passed 556/556, zero failed/skipped. Metadata and diff checks
passed. DocsCheck initially rejected the new document before it was tracked;
that output is retained, and the staged documentation receives its final check.
The follow-up hosted gate is recorded in the PR/handoff without moving the
validated tip solely to record CI.

Native/server/codec inputs and workflows remain unchanged. Original native
hosted success belongs to its original checkout; a follow-up head requires its
own ordinary hosted checks. No frozen DEV artifact is rebuilt or relabelled.
Existing AGENTS already require explicit test seams and deterministic ownership
coverage; no new agent policy is introduced. P7.2–P7.6 and Phase 8 remain unstarted.
