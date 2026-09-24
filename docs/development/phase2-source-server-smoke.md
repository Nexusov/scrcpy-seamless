# Phase 2 source-built server DEV smoke

On 2026-09-24, the owner performed one physical USB-to-Wi-Fi recovery with a
separate `DEV SOURCE-SERVER SMOKE` copy. Video, PC control and PC audio worked
before USB removal and after Wi-Fi recovery. This validates the tested DEV
combination; it does not change the canonical 1.x release package or prove that
the earlier intermittent audio observation cannot recur.

## Tested inputs

| Input | Evidence |
| --- | --- |
| Source commit | `d13b6385dd53b93a9aea2accc4f392dbdd7cc06b` on `2.0/p02-build` |
| DEV copy | `dist/dev/scrcpy-seamless-win64-dev-source-server-p02-gd13b638/` (ignored, local only) |
| Source-built native `scrcpy.exe` | SHA-256 `969f399dfae8ad12cf55f252ddb1d3f2dd67f03e74b6da87b9f8c8ff76c9592e` |
| Source-built Android server | SHA-256 `a5e307a072dac91a766e929733d187c531d30271eb3a7b19cbd6923349685c47` |
| Clean base DEV ZIP | SHA-256 `14e5a52feca6747c3a22adc46cf2e5c7c2a94e270ed8b7fd56f00b570c80413b` |

The copy was extracted from the clean Phase 2 DEV ZIP. Its two binaries above
replace the ZIP's native executable and reviewed imported server. ADB, SDL and
FFmpeg binaries remain byte-identical to that ZIP. The copy has its own
configuration, logs, shortcuts and instance mutex; it uses the previously
owner-approved shared ADB daemon and trust. Its `dev-build.json` and
`source-server-build.json` identify the source-built inputs. The retained
release manifest describes the canonical 1.x baseline, not approval of this
DEV server for release.

## Observed transition

The ignored local trace is
`work/phase0/observations/20260924T151419Z-c3510eaa/trace.jsonl`, SHA-256
`b33af24b7adcc321e8b43bd5cadc4ebd7c26adde739d5bf0ac3df5b4860e735f`.
It finished normally (`capture_end`, line 1056). It records the DEV native
SHA-256 above at capture start. Do not commit the trace, ADB logs or the local
device configuration.

The native process had PID `32112`, start time `15:16:59.258745Z` and HWND
`10879814` before removal. The sampled process immediately before the
`usb_removed` marker (15:18:51.756Z) and after the owner's audio-restored
marker (15:19:29.557Z) had the same PID, start time and HWND. Samples retained
those values through the end of capture; the owner's separate path-based
PowerShell query after recovery also returned PID `32112` and HWND `10879814`.
There was no separate manual PID query before removal, so the pre-removal value
comes from the observer.

The trace and native log show USB selected first, followed by server
disconnection, a TCP/IP connection, and resumption in the existing window.
The owner marked video restored at `15:19:25.199Z`, control working at
`15:19:26.677Z`, and audio restored at `15:19:28.150Z`. These markers and the
owner's report establish working video, control and audible PC output after
the transition. Native log events carry collector-poll timestamps and were
read in buffered batches; neither those timestamps nor the delayed manual USB
marker establish an exact failover duration.

The collector classified 201 `audio_buffer_drop` messages across the capture,
both before and after the switch. No audible-glitch marker was recorded, and
the owner reported working audio. These messages alone do not establish an
audio defect or its cause. This was one successful source-server cycle, not a
soak test or proof of interruption-free media. The prior
[audio A/B classification](phase2-audio-ab.md) remains unchanged.
