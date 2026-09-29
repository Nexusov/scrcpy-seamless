# Phase 6C DEV manual acceptance proposal

This procedure is prepared for the local package built from source commit
`d9d12a12bc94bd5b272824631d38e0cb6be1a4f0`. It has **not** been run
against a phone. The package is
`dist/dev/scrcpy-seamless-desktop-p06c-gd9d12a12/`; its ZIP is
`dist/dev/scrcpy-seamless-desktop-p06c-gd9d12a12.zip` with SHA-256
`e33c68d863dae2e700a1839e9bcb0df0546ce6affc1bc9535522bdc117533f00`.
The runtime manifest in that directory records the exact machine-contract
claim and every native/server/ADB/SDL/FFmpeg hash and origin. ZIP extraction
and complete inventory verification passed from outside the repository.

Use only the isolated `.dev-data/p06c` root. Do not copy profiles or ADB keys
from another installation. An already paired device need not be paired again.
Do not stop/restart the shared ADB server for this procedure.

From PowerShell, after setting the repository path:

```powershell
$repository = 'D:\My Projects\scrcpy-seamless'
$package = Join-Path $repository 'dist\dev\scrcpy-seamless-desktop-p06c-gd9d12a12'
$data = Join-Path $repository '.dev-data\p06c'
$desktop = Join-Path $package 'ScrcpySeamless.Desktop.exe'
& $desktop --preview --page=settings
& $desktop "--dev-data-dir=$data" --page=settings
& $desktop "--dev-data-dir=$data" "--device-runtime=$package\runtime" --page=devices
```

Close each window before launching the next mode. Preview must show simulated
data and leave the selected data root unchanged; settings-only must remain
editable without a native child or ADB action. Device-enabled startup should
show the machine host but must not refresh, pair, connect or mirror until an
explicit action.

For the supervised device run, connect the already paired phone by USB with
Wi-Fi debugging enabled. Refresh, select the eligible USB row and create or
select a profile in the isolated root. When creating one, use the selected USB
transport in the profile draft, enter the connection endpoint from the main
Wireless debugging screen if fallback is intended, then **Save to draft** and
**Apply**. Confirm the machine host is selected. Mirror and independently
check updating video, usable PC control and audible PC output. A video
lifecycle event alone establishes none of the latter two.

Use the exact package path below before USB removal and after Wi-Fi recovery
to record native PID, creation time, window handle and title. Compare all
three identity values; a new process or window is a failure of the proposed
same-child transition.

```powershell
$nativeExecutable = Join-Path $package 'runtime\scrcpy.exe'
Get-CimInstance Win32_Process |
    Where-Object { $_.ExecutablePath -ieq $nativeExecutable } |
    ForEach-Object {
        $windowProcess = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
        [pscustomobject]@{
            PID = $_.ProcessId
            Created = $_.CreationDate
            HWND = $windowProcess.MainWindowHandle
            Title = $windowProcess.MainWindowTitle
        }
    } | Format-List
```

With a continuous local audio source on the phone, unplug USB and check video,
control and PC audio separately after Wi-Fi recovery. Record any sound on the
phone and whether its playback timer advances if audio fails. Check that the
SessionId remains fixed and that the connection attempt changes in ordered
machine lifecycle observations. These IDs and the full observation order are
currently internal to the Desktop's bounded projection, not visible in the
normal UI; obtain them through a supervised read-only debug observation or
mark this part **unverified**. Do not infer attempt identity from PID, elapsed
time or a displayed video frame. Stop and confirm exact-child exit.

In a separate run, leave the saved network endpoint but disable fallback in
the committed profile. Start on USB, disconnect USB and verify that it does
not switch to Wi-Fi. A selected network route may still retry on that same
route. Then compare an ordinary native-window close with Desktop Stop and
normal Desktop close: record reported reason, exact child exit, retained
configuration and no surprise relaunch. Test the explicit **Focus mirror**
button separately from launching a second Desktop instance with the same data
root; `Applied` reports a focus attempt, not guaranteed foreground activation.

The production native parent-death guard has synthetic process coverage. An
abnormal Desktop-death experiment on a phone-backed mirror requires separate
authorization and is outside this proposal. Preserve sanitized diagnostics
before retrying a new failure. No result from the Phase 5 artifact transfers
to this machine-route artifact.
