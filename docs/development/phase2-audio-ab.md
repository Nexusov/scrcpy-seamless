# Phase 2 audio recovery A/B investigation

2026-09-24, local hardware investigation. This note follows the
[root-cause method](debugging.md). It records a failed initial Phase 2 smoke
and subsequent controlled comparisons without assigning an unproven cause.
The owner has not accepted Phase 2 or authorized Phase 3.

## Builds and runtime composition

| Build | Source Git SHA | Local DEV ZIP | ZIP SHA-256 | `scrcpy.exe` SHA-256 |
| --- | --- | --- | --- | --- |
| A, accepted Phase 0 hardware artifact | `ea193f21c0b179081909a18d2bda4f404036530e` | `dist/dev/scrcpy-seamless-win64-dev-gea193f2.zip` | `45317fdc5adf2220031a55a544fe8b90176f775bd5315123defc61283248bef3` | `3ce246ffe840f4b662a847c23f2bd085158ab177698c381a21c7078239f55d5b` |
| B, Phase 2 local hardware artifact | `7b140f329ee83a08ae525b60f46183254274a516` | `dist/dev/scrcpy-seamless-win64-dev-p02-g7b140f3.zip` | `14e5a52feca6747c3a22adc46cf2e5c7c2a94e270ed8b7fd56f00b570c80413b` | `299dd982d2f15b9300ef36c60a0d5e364aa33d6aec2a858935e097bda4f705c5` |

The following packaged runtime files have **identical bytes** in the two ZIPs.
Hashes were checked against the ZIP entries, not inferred from manifests:

| Packaged component | Version / role | Shared SHA-256 |
| --- | --- | --- |
| `scrcpy-server` | reviewed imported scrcpy 4.0 Android server | `84924bd564a1eb6089c872c7521f968058977f91f5ff02514a8c74aff3210f3a` |
| `adb.exe` | ADB 1.0.41, 34.0.5-10900879 | `58765259a349cce392fbb2f15dab75fed3b7c0b40cc68a7653278b9850602a2f` |
| `AdbWinApi.dll` | ADB support | `689e4263252c734ee40d748f0e5a911801c6083a8e81b5040fd9c49dff3bfdce` |
| `AdbWinUsbApi.dll` | ADB support | `e6141805bb19eeafac6ab2d0fb50aa098b8c27149dc8ed73739cc40436274748` |
| `SDL3.dll` | SDL 3.4.8 | `f8bb1698f618949498ac517a0766eaa91972ecc818d80deb00634e197ee923cf` |
| `avcodec-62.dll` | FFmpeg 62.28.101 | `893237890f744ea1eb447f56e8ac9d803deb48170bb456a11a10edf8c08a1eaa` |
| `avformat-62.dll` | FFmpeg 62.12.101 | `d46e9b99b27c743b8ae73eacfd76004fba218b65eba5a1ce9cdf494a7030a4a4` |
| `avutil-60.dll` | FFmpeg 60.26.101 | `2933c61bd5c3f0c2bec22310de8b9a22969030fb1ee204eae1a4948e7599f59d` |
| `swresample-6.dll` | FFmpeg 6.3.101 | `91c2595781581c61144262fc596dcdbdca7f72b6315d28e31becc67d35b2cf59d` |

Neither package has a separate libusb DLL; both native builds use `-Dusb=false`
and ordinary USB mirroring through ADB. Both use the same GCC 16.2.0 compiler
binary, FFmpeg and SDL development versions, and principal Meson options
(`debugoptimized`, `compile_server=false`, `portable=true`, `usb=false`,
`b_lto=false`). `release-manifest.json` is identical. DEV overlay differences
are limited to the instance mutex and displayed Git label. Launcher session,
configuration and option logic are unchanged. The separately source-built Phase
2 Android server (`a5e307a072dac91a766e929733d187c531d30271eb3a7b19cbd6923349685c47`)
was **not** placed in B. Mixing old/new server, ADB, SDL or FFmpeg cannot
distinguish these two packages as shipped.

The substantive runtime variable is the native executable. Its source delta
consists of the isolated initial-video-window size/event ownership guard
(`ff4e1bf`) and checked audio callback-buffer allocation (`3789287`). The
latter calls `sc_allocarray()`, which checks multiplication overflow and then
calls `malloc()`; it does not otherwise change the normal allocation path.
This source review is not proof that either change caused or could not cause
the observed failure.

## Reproduction and observations

The first B smoke produced video, PC control and PC audio on USB, with audible
speed/skip artifacts. After USB removal, video and control recovered over
Wi-Fi in native PID `36980` and HWND `2691502`, but PC audio did not. A later
separate launch had PID `19792` and HWND `5311488`. The user clarified that
audio normally routes to the PC without duplicating on the phone; phone
silence therefore adds no independent failure signal. Whether the playback
source and its timer continued during the initial failed run was not captured.

For the controlled A/B, the owner used the same Android 16 (SDK 36) phone,
combined USB and Wi-Fi mode, the same separate DEV settings, and a local audio
source set to repeat. The test launches used only DEV packages. Each assessed
cycle started with USB, confirmed video/control/PC audio, physically removed
USB while playback continued, and assessed Wi-Fi recovery. The local
`scripts/observe-dev.ps1` sampled exact-package PID/HWND, categorized ADB
availability and sanitized native events; run-specific raw log copies were
retained in ignored `work/phase2/ab-audio/<run>/` directories. Collector
timestamps are not native event times.

| Assessment cycle | PID | Nonzero HWND | USB video/control/PC audio | Wi-Fi video/control/PC audio | Native log: USB/TCP selections; audio demuxer starts; sample-drop messages |
| --- | ---: | ---: | --- | --- | --- |
| A1 | 37068 | 3151208 | yes | yes | 1/1; 2; 14 |
| B1 | 36540 | 2361956 | yes | yes | 1/1; 2; 9 |
| A2 | 42956 | 3408772 | yes | yes | 1/1; 2; 8 |
| B2 | 33096 | 527630 | yes | yes | 1/1; 2; 193* |
| A3, clean repeat | 19804 | 2163148 | yes; PC audio appeared after a short delay | yes | 1/1; 2; 5 |
| B3 | 25656 | 3539806 | yes | yes | 1/1; 2; 2 |

Each listed run retained one PID and one nonzero HWND through its observed
transition; there were no unresponsive process samples. ADB samples showed
USB availability followed by network-only availability within each run. The
native logs have no explicit audio-device-open or audio-stream error in these
six runs. Underflow was not measured: its regulator log is disabled without
`SC_AUDIO_REGULATOR_DEBUG`. The counts are neither rates nor a measure of
audibility. *B2 remained open after its successful assessment and overlapped
with a subsequent A launch; its saved log includes that later period, so its
193 drop messages cannot be compared with the other cycles.*

That overlapping A launch (PID `40036`) produced accelerated/duplicated video
and audio, then missing audio, while B (PID `33096`) was still running. It is
excluded from the A/B result because two native clients could compete for
the same device/audio source. The clean A3 repeat followed after both were
closed. The first B smoke has no evidence establishing such an overlap; do
not retrofit this explanation to it.

The accepted Phase 0 hardware observations included audible recovery; its
separate trace had sample-drop messages but lacked synchronized owner markers,
so it cannot place those messages relative to audible recovery. The failed B
trace had drops before and after its reconnect. In
[`audio_regulator.c`](../../src/scrcpy/app/src/audio_regulator.c), this message
requires a prior SDL audio callback pull, so post-reconnect logs indicate
decoded frames reached the regulator and SDL requested data. They do not show
whether captured samples were non-silent or Windows actually emitted sound.
No log contains first-packet timing, decoded/played PCM level, Windows output
state or a reliable original playback-source marker.

## Classification and Phase 2 gate

**C — insufficient evidence** to classify the first B failure as a Phase 2
regression or a pre-existing 1.x defect. Clean A/B recovery was 3/3 for each
artifact, while the original B failure remains a valid, non-reproduced hardware
observation. The package comparison excludes server and runtime libraries as
A/B variables, but cannot exclude environment or session state. The
concurrent-client A failure is a separate confounded observation, not proof
that the first B failure was already present in 1.x.

- **Symptom:** PC audio absent after the initial B USB-to-Wi-Fi reconnect while
  video and control recovered.
- **Trigger:** physical USB removal with Wi-Fi available in that one run.
- **Immediate defect / first incorrect boundary:** undetermined. Decoder-to-
  regulator activity occurred, but source content and audible output were not
  measured.
- **Intended invariant:** if media playback continues and audio forwarding is
  enabled, a recovered session should eventually provide PC audio. The first
  run did not record continued playback, so even its preconditions are not
  fully established.
- **Enabling cause:** undetermined; buffered unstructured logs and missing
  source/PCM/sink markers prevent localization.
- **Why prior validation missed this uncertainty:** Phase 0 accepted one
  qualitative audio return, and unit/build tests do not exercise physical
  failover or audible output.

No production fix or regression test was made because the defective boundary
and a stable regression have not been identified. If the owner requests more
investigation, the narrow next step is a timed, single-process reproduction
with an explicitly continuous source plus temporary aggregated packet/PCM
level and SDL/Windows sink diagnostics. Do not mask the symptom with sleeps,
blind restarts or broad audio changes. Phase 2 remains unaccepted; P2.9 cannot
be closed as fully green from these results. Phase 3 remains out of scope.
