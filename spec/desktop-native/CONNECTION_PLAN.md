# Immutable machine launch connection plan, version 1

`--seamless-connection-plan=<JSON>` is required only with the explicit machine
bootstrap. It is one `ProcessStartInfo.ArgumentList` element, not shell text,
an environment variable, saved configuration or framed protocol message.
The existing version-1 IPC framing, capabilities and exact-integer rules remain
unchanged. An older native which does not accept the option fails before a
device session rather than receiving a hidden fallback policy.

The immutable committed `NativeStartRequest.Plan` supplies candidates and policy.
The explicitly selected eligible route is moved to index zero. Its actual ADB
selector is retained as `serial`, including for network transport; transport is
never guessed from that selector's spelling. No profile ID, alias, pairing
endpoint, pairing code or configuration revision is included.

The object has exactly these properties (no duplicates or extensions):

```json
{"version":1,"reconnect":true,"selected":0,"requiredCapabilities":5,"desiredCapabilities":7,"maximumAttempts":3,"initialBackoffUs":"1000000","maximumBackoffUs":"8000000","candidates":[{"transport":"usb","serial":"SYNTHETIC_USB"},{"transport":"network","endpoint":"synthetic.example:37123"}]}
```

- UTF-8 JSON is at most 4096 bytes, parsed in a fixed 256-KiB memory budget.
- Version is integer 1; selected is integer 0. Reconnect is a required boolean.
- Required/desired capabilities are integer masks 1..7 for video/audio/control;
  required is a subset of desired. Admission does not establish channel readiness.
- Maximum attempts is integer 1..100, including the first attempt in a retry
  episode. Durations are canonical unsigned decimal **strings**, in microseconds,
  0..86400000000. Maximum backoff is at least initial backoff. Managed TimeSpan
  ticks must convert exactly; sub-microsecond values are rejected, never rounded.
- One or two candidates are permitted. Two candidates must use different
  transports. Presence of a second candidate represents the committed fallback
  permission. Reconnect=false disables retry even after stable-health reset.
- USB requires a nonempty serial and forbids network fields. Network requires
  `service` and/or `endpoint`, with optional selected `serial`. A service is an
  bare ADB instance or an ADB TLS connection service (optional trailing dots),
  never an explicitly typed pairing/other service. Positive ADB resolution and
  endpoint validation remain the resolver's responsibility.
- Serial/service identifiers are at most 256 UTF-8 bytes; endpoints at most 259
  bytes, preserving the existing maximum hostname-plus-port representation.
  A selected network serial which is itself a validated endpoint uses the same
  259-byte endpoint bound; other opaque serials and all USB serials keep 256.
  NUL/control characters are rejected. Oversized inputs are rejected before
  native creation; stored configuration is not normalized or truncated.
- Automatic failback is unsupported and rejected before process creation.

Native parsing copies the complete accepted value into invocation-owned storage.
The options pointer borrows that storage until application retirement completes;
no JSON document, argv slice or generation-owner pointer is retained in the
parsed plan. Invalid parses do not modify the destination.

This carrier removes `SCRCPY_RECONNECT_SERIAL` and `ANDROID_SERIAL` inheritance
from the machine host. The legacy host and ordinary CLI remain distinct paths;
this change is not a claim that all runtime path/environment handling is removed.
No new public option, protocol command, second reader or diagnostic export is
introduced. The connection manager owns retry decisions and the resolver owns
external target discovery; the carrier itself performs neither.

## Independent source validation

`test_connection_plan` links the production parser and checks malformed input,
destination immutability on rejection, UTF-8 ownership and endpoint syntax.
`test-ipc-contract.ps1 -ConnectionPlanHarnessPath <Meson test executable>` also
exports five actual committed managed requests through the production serializer.
The native harness verifies their policy, explicit selection, fallback permission,
service/endpoint and UTF-8 boundary values against independently written C
expectations. Fifteen mutations of those actual bytes verify duplicate, unknown
and invalid capability rejection. Managed capability rejection occurs at the
typed Core policy constructor; there is no separate managed launch-JSON reader.
The hosted native job invokes this lane after building the actual target.
Its totals remain separate from the unchanged bidirectional wire-v1 vectors.
