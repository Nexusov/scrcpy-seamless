# Debugging and root-cause analysis

Bug fixing in scrcpy Seamless is evidence-driven. The goal is not merely to
make a visible symptom disappear; it is to understand and repair the violated
invariant without creating a new failure class.

## Investigation order

For every non-trivial bug, follow this sequence unless a documented constraint
makes a step impossible.

### 1. Define the symptom

Record:

- expected behavior;
- actual behavior;
- reproducibility;
- trigger;
- affected version/environment;
- device/transport/session state when relevant.

### 2. Reproduce

Attempt to reproduce before modifying production logic.

Prefer the smallest deterministic reproduction.

If the issue cannot be reproduced, do not invent a root cause. Gather more
evidence, add targeted diagnostics if necessary, and state competing
hypotheses/uncertainty.

### 3. Reduce

Find the first boundary where state becomes incorrect:

- Desktop UI;
- Core/Application;
- Infrastructure;
- ADB/discovery;
- Desktop-native IPC;
- native application lifetime;
- native session lifetime;
- transport;
- video;
- audio;
- controller/input;
- Android server;
- device protocol.

### 4. Observe

Use structured evidence:

- timestamps;
- AppInstanceId / SessionId / ConnectionAttemptId;
- state transitions;
- protocol messages;
- logs;
- thread/handle counts;
- debugger/profiler output;
- sanitizer results.

Temporary instrumentation is allowed. Remove noisy temporary instrumentation
afterward or convert useful signals into intentional diagnostics.

### 5. Form competing hypotheses

Write down plausible causes and the distinct predictions they make.

Do not edit production code merely because the first hypothesis sounds
plausible.

### 6. Disprove alternatives

Run the cheapest experiment that distinguishes the hypotheses. Change one
variable at a time when practical.

### 7. Identify the root cause

For meaningful failures distinguish:

- **Symptom** — what the user observed.
- **Trigger** — what made the failure visible.
- **Immediate defect** — the concrete incorrect state/operation.
- **Violated invariant** — the rule the architecture expected but failed to
  enforce.
- **Enabling/systemic cause** — why the architecture allowed the defect.
- **Escape** — why tests/review/validation did not catch it.

Not every cosmetic bug needs all six sections, but concurrency, lifetime,
protocol, data-loss, and release-integrity bugs usually do.

## Regression test before the fix

Where reasonably possible:

1. add a regression test;
2. verify it fails on the buggy implementation for the expected reason;
3. apply the production fix;
4. verify the test passes.

Name tests after behavior/invariants rather than ticket numbers.

If real hardware is required, create the strongest deterministic synthetic
coverage possible and maintain a hardware validation scenario. Be explicit
about what remains hardware-only.

## Fix the cause, not the symptom

"Smallest fix" means the smallest correct repair of the violated invariant,
not the fewest changed lines.

Examples of bad substitutes for understanding:

- increasing a reconnect sleep to hide a race;
- blindly retrying a deterministic invalid operation;
- adding a null check when stale ownership is the real bug;
- killing a process broadly instead of fixing shutdown ownership.

If the bug demonstrates a missing architectural boundary, an architectural fix
may be safer than repeated guards.

## Prohibited shortcuts

Do not make a bug "go away" by:

- swallowing exceptions;
- ignoring return values;
- disabling assertions;
- weakening/deleting tests;
- increasing arbitrary sleeps/timeouts;
- adding unconditional retries;
- resetting global state without understanding ownership;
- broad process termination;
- continuing after unknown fatal state;
- suppressing sanitizer/compiler warnings.

Any exception requires evidence that the new behavior is itself correct.

## Git as a diagnostic tool

For regressions use, as appropriate:

```bash
git log
git show
git blame
git diff
git bisect
```

Use history to understand causality, not assign blame.

## Concurrency/lifetime failures

Do not use sleep-based reproduction as the primary tool.

Prefer deterministic:

- barriers/events;
- controlled adapters;
- fake clocks;
- lifecycle hooks;
- explicit cancellation;
- repeat/stress harnesses.

Inspect:

- owner;
- producer lifetime;
- consumer lifetime;
- task/thread owner;
- cancellation owner;
- stop/join/destroy ordering;
- session/generation identity;
- queued stale work;
- lock ordering;
- locks held across blocking I/O/callbacks.

A race is not fixed merely because repeated manual attempts no longer reproduce
it.

## Native memory/resource failures

Inspect:

- use-after-free;
- double free;
- uninitialized state;
- bounds;
- HANDLE/fd/socket ownership;
- FFmpeg object lifetime;
- SDL resource lifetime;
- thread shutdown.

Use sanitizers and repeated lifecycle/soak tests when available.

After a leak fix, verify resource usage does not monotonically grow across
repeated cycles.

## IPC/protocol failures

Capture the semantic sequence and verify:

- protocol version;
- message type;
- frame length;
- SessionId / ConnectionAttemptId;
- ordering;
- duplicate/stale messages;
- malformed-input behavior.

Update golden vectors/parser tests where appropriate.

Do not patch only one protocol side and silently create an incompatibility.

## ADB/transport failures

Capture representative external output as sanitized fixtures where useful.

Cover:

- authorized;
- unauthorized;
- offline;
- multiple devices;
- mDNS changes;
- IPv4/IPv6/hostname;
- daemon startup/restart;
- timeout;
- disconnect/reconnect.

Do not build parsing around one happy-path output string.

## Configuration/migration failures

Inspect:

- schema version;
- source revision;
- partial writes;
- atomic replace behavior;
- interrupted migration;
- malformed config;
- backups/rollback;
- old-version fixtures.

Never destructively "repair" user data without a recoverable strategy.

## UI failures

First decide whether the defect is:

- domain/application state;
- ViewModel projection;
- binding;
- layout/rendering.

Do not patch XAML visibility if the underlying application state is already
incorrect.

Use the appropriate layer: unit, Avalonia.Headless, visual regression, or E2E.

## Performance failures

Measure before and after. Record the workload.

Inspect CPU, latency, allocations, memory, handles, threads, blocking
operations, and frame timing as appropriate.

Do not trade correctness for a benchmark improvement.

## Flaky tests

A flaky test is a defect.

Do not hide flakiness with blind retries or large sleeps. Determine whether the
cause is a product race, test race, external nondeterminism, timing dependence,
or leaked resource.

## Validate the blast radius

After a fix:

1. run the direct regression;
2. run subsystem tests;
3. test adjacent lifecycle states;
4. test failure/cancellation/shutdown paths where relevant;
5. run broader integration tests if shared infrastructure changed;
6. run sanitizer/soak/hardware validation when warranted by the bug class.

Do not stop at "the original reproduction no longer fails."

## Root-cause report

For a meaningful bug fix report:

- Symptom
- Reproduction
- Root cause
- Violated invariant
- Why old code allowed it
- Fix
- Regression test
- Additional validation
- Risk
- AGENTS/docs impact

Keep trivial cosmetic fixes concise.

For serious security, data-loss, release-integrity, crash-loop, or
lifetime/concurrency incidents, persist a root-cause note when it provides
future value.

## Done condition

A bug is resolved only when the cause is understood to a reasonable
engineering standard, the fix addresses it, appropriate regression coverage
exists, neighboring behavior is validated, and no warning/test suppression
hides the issue.

If the root cause remains uncertain, say so explicitly.
