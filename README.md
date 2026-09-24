# scrcpy Seamless

**scrcpy Seamless** is a portable Windows x64 GUI fork of [scrcpy 4.0](https://github.com/Genymobile/scrcpy/releases/tag/v4.0) with guided phone setup, graphical
settings, and automatic USB-to-Wi-Fi fallback in USB + Wi-Fi mode.

Mirror and control your Android phone over USB, Wi-Fi, or both without terminal commands or manual
configuration files. Root access and Android app installation are not required.

scrcpy Seamless is an independent project and is not an official [Genymobile](https://github.com/Genymobile/scrcpy) release.

## Features

- Connect over USB, Wi-Fi, or USB + Wi-Fi, including Wi-Fi setup without a USB cable.
- Configure the phone through a guided setup wizard instead of terminal commands or manual JSON
  editing.
- Adjust common mirroring options in a graphical interface and browse the full scrcpy 4.0 option
  catalog in **Advanced**.
- In USB + Wi-Fi mode, keep the same mirroring window and last frame visible while automatically
  falling back to Wi-Fi, then resume video, audio, and control.
- Save phone setup and mirroring preferences between launches.
- Bring an existing setup, connection, or mirroring window forward instead of starting a second
  session.
- Optionally create or update a desktop shortcut and run without a separate console window.

## Quick start

1. Open the [latest release](https://github.com/Nexusov/scrcpy-seamless/releases/latest) and download `scrcpy-seamless-win64.zip`. The dependency source archive and GitHub's
   **Source code** downloads are not required to run the application.
2. Extract the ZIP into an empty, writable folder. Do not run the application from inside the
   archive and do not extract a new version over an older package.
3. Double-click `Start.vbs`, choose a connection mode, and follow the setup wizard.

Optionally compare the ZIP with the matching `.sha256` file from the same release before extracting
it.

Mirroring starts when setup finishes. On later launches, use `Start.vbs` or your desktop shortcut.

The application is portable. Keep the top-level launcher files and the `app/` folder together.
Runtime binaries and launcher modules are stored under `app/`; do not move its files out
individually.

## Choose a connection

| Mode | What you need | How it works |
| --- | --- | --- |
| **USB** | A data-capable cable and USB debugging | Mirrors over USB. Wi-Fi setup is skipped. |
| **Wi-Fi** | Android 11+ and Wireless debugging | Pairs and mirrors without a USB cable. |
| **USB + Wi-Fi** | Both of the above | Prefers USB at startup and falls back to Wi-Fi if USB disconnects. Recommended if you want Seamless reconnection. |

### USB

1. Enable **Developer options** and **USB debugging** on the phone.
2. Connect it with a data-capable USB cable, unlock the phone, and accept the USB debugging
   authorization prompt. Allow the computer permanently if you trust it.
3. Choose **USB only** in the setup wizard, select the phone, and finish setup.

The location of Developer options varies by manufacturer. If the phone does not appear, check the
cable, accept the authorization prompt, and click **Refresh**. Some phones require a manufacturer USB
driver on Windows.

### Wi-Fi

1. Connect the phone and PC to the same network.
2. On the phone, open **Developer options > Wireless debugging > Pair device with pairing code**. Keep the pairing dialog open.
3. Choose **Wi-Fi only (no USB cable)** in the setup wizard, enter the six-digit pairing code, and finish setup.

The wizard tries to discover the phone automatically. If discovery fails, choose **Enter addresses manually**; see
[Wi-Fi troubleshooting](#wi-fi-troubleshooting).

A USB cable and USB debugging authorization are not required for Wi-Fi-only setup.

### USB + Wi-Fi

Complete both USB authorization and Wi-Fi pairing in the setup wizard.

With **Seamless reconnection** enabled, scrcpy Seamless uses USB when it is available at startup and reconnects
over Wi-Fi if the cable is removed or the USB connection is lost.

Phone setup is saved for later launches.

## Seamless USB-to-Wi-Fi reconnection

In **USB + Wi-Fi** mode with **Seamless reconnection** enabled, losing the USB connection does not close the mirroring
window.

The window keeps the last frame visible and shows `Reconnecting...` while waiting for the phone over Wi-Fi.
Video, audio, and control resume after reconnection.

Wi-Fi must remain available on both the phone and PC. See [Limitations](#limitations) for current reconnection
restrictions.

## Requirements

- Windows x64 with Windows PowerShell 5.1 and Windows Script Host enabled.
- Android 5.0 or later for standard mirroring.
- Android 11 or later for audio forwarding and the Wireless debugging setup described above.
- Android 12 or later for camera mirroring.
- For USB: USB debugging authorization, a data-capable cable, and a device driver if required.
- For Wi-Fi: the phone and PC must be on the same network and the network must allow them to
  communicate with each other.

ADB and the required runtime libraries are included in the portable ZIP. No installation, compiler,
or development tools are required.

## Settings

Open `Settings.vbs` to configure the application.

### Connection

Use the **Connection** tab to:

- change the saved phone or connection mode;
- set up another phone;
- reset the saved phone setup;
- create or update a desktop shortcut.

A previously saved Wi-Fi configuration can be reused without repeating pairing while the phone still
remembers the PC. Creating a desktop shortcut does not require the phone to be connected.

### Mirroring

The **Mirroring** tab contains commonly used options:

- maximum resolution;
- video bit rate;
- maximum FPS;
- audio;
- Seamless reconnection.

Blank fields use scrcpy defaults.

For example, you can set maximum resolution to `1280`, video bit rate to `4M`, and maximum
FPS to `60`. The FPS setting is an upper limit: it cannot turn a 30 FPS source into 60 unique
frames.

Click **Save mirroring settings**, then restart mirroring to apply changes. The phone does not need to be connected
to save these settings. Closing Settings without saving discards mirroring edits.

### Advanced

The **Advanced** tab exposes the full scrcpy 4.0 option catalog, grouped into Video, Audio, Window,
Control, Device, Camera, Recording, and Technical categories.

Search by option name, CLI flag, or description, then select a parameter to read its explanation and
edit its value. Switching between parameters or filtering the list preserves unsaved values until
you save or close the window.

Informational actions such as listing encoders, cameras, displays, and apps open a results window
instead of saving a launch option. Device-specific lists require saved setup and a connected phone;
help and version information work offline.

Options managed by the connection wizard and features unavailable in this Windows build remain
visible with an explanation instead of being editable. Settings also explains common option
conflicts before saving. scrcpy performs the final device-specific capability checks when a session
starts.

#### Camera

Camera mirroring requires Android 12 or later.

Select `camera` as **Video source** in **Advanced**, then configure the available camera options. Camera
and codec availability depends on the phone.

#### Recording

Recording requires **Seamless reconnection** to be disabled.

Set a recording filename in **Advanced**. Relative filenames are saved in `app/`; you can also
enter an absolute path. The destination folder must already exist and be writable.

With **No window** enabled, the launcher remains visible so the session can be stopped normally. Normal
shutdown is requested first so an active recording can finish writing its file.

### Restore or reset settings

**Restore mirroring defaults** resets mirroring preferences in the current Settings window. Click **Save mirroring settings** to apply
the reset. It does not remove the saved phone or Wi-Fi pairing.

**Reset device setup...** removes the application's saved phone and connection configuration. Close any running
mirroring window first, then confirm the reset. You can configure a phone again immediately or on
the next launch.

If the connection window is already waiting, save the new setup and then click **Retry now** in that
window.

Resetting device setup keeps mirroring preferences, desktop shortcuts, logs, and shared ADB keys. It
does not remove pairing from the phone or affect other ADB applications.

To forget this PC on the phone as well, open **Wireless debugging > Paired devices**, select the PC, and choose **Forget**.

Mirroring preferences are stored in `app/scrcpy-settings.json`; saved phone setup is stored separately in
`app/phone.json`.

## Troubleshooting

### Connection window

A connection window appears when the application starts and waits for the requirements of the
selected connection mode.

- **Retry now** reloads the saved setup and starts another connection check without creating a second
  mirroring session.
- **Settings** opens the setup wizard.
- **Cancel** stops waiting.
- **Open logs** opens the diagnostics folder.

Launching scrcpy Seamless again while setup, connection, or mirroring is already active brings the
existing window forward instead of starting another session.

If the native mirroring process starts but its window does not appear within 30 seconds, the
launcher stops that attempt and offers a retry. This timeout applies only to the failed launch
attempt; it does not limit how long the connection window can wait for the phone.

### Wi-Fi troubleshooting

**Automatic discovery cannot find the phone:** Wireless debugging discovery relies on mDNS. If the phone and PC can communicate but
automatic discovery fails, choose **Enter addresses manually**. Guest, enterprise, or otherwise isolated Wi-Fi
networks may block device-to-device traffic entirely; in that case, use a different network.

**Pairing cannot find the phone:** copy **Pairing IP:port** from the phone's **Pair device with pairing code** dialog and enter a fresh six-digit code. If
the dialog was closed or the code expired, open it again.

**Pairing succeeds, but connection fails:** enter **Connection IP:port** from the main **Wireless debugging** screen. This uses a different port from the
pairing port. Both addresses must belong to the same phone.

For example, an address may look like `192.168.1.10:37000`; always use the actual values shown on the phone.

If pairing has already succeeded during the current setup session, retry the connection without
entering another pairing code.

**A saved Wi-Fi connection stops working:** make sure Wireless debugging is still enabled and both devices are on the same network. A
manually entered connection address can change after a network change or after Wireless debugging is
restarted. Open `Settings.vbs` to update it, and pair again if the phone has forgotten the PC.

**Low FPS, stuttering, or choppy audio over Wi-Fi:** compare the same content over USB first. If USB is smooth, the problem is likely
network-related.

Try switching between **5 GHz and 2.4 GHz Wi-Fi**; either band may work better depending on the router, distance,
interference, and phone. If your router exposes channel-width settings, you can also try a fixed
width instead of **Auto**. On 5 GHz, **40 MHz or 80 MHz** are reasonable values to test. Change one setting
at a time and restore it if there is no improvement; there is no universally best band or channel
width.

Reducing maximum resolution or video bit rate can also help determine whether available Wi-Fi
throughput is the limiting factor.

After changing Wi-Fi or router settings, the Wireless debugging address may change. Check it again
if the saved connection no longer works.

### Control troubleshooting

If mirroring works but keyboard or mouse control fails with an `INJECT_EVENTS` permission error, some
devices, especially Xiaomi models, require an additional **USB debugging (Security Settings)** option in Developer options.
Enable it and reboot the phone.

### Audio troubleshooting

Audio forwarding requires Android 11 or later. On Android 11 specifically, keep the phone unlocked
when starting scrcpy; otherwise audio capture may fail. If audio capture is unavailable, normal
mirroring can continue with video only.

### Logs and local data

For launch errors, check `app/last-run.log` and `app/last-run-errors.log`. Setup errors are also shown in the wizard.

Saved phone setup is stored locally in `app/phone.json`. Mirroring preferences are stored in `app/scrcpy-settings.json`.
Pairing codes are not saved.

ADB authorization uses the standard shared ADB trust/key mechanism. Resetting scrcpy Seamless device
setup does not remove those shared ADB keys.

Release archives do not contain personal phone settings, mirroring preferences, or logs.

## Limitations

- Automatic transport switching currently works from USB to Wi-Fi in USB + Wi-Fi mode with
  **Seamless reconnection** enabled. Switching back from Wi-Fi to USB requires restarting the application.
- Device control is unavailable while reconnecting. The mirroring window may briefly stop responding
  while the previous native session shuts down.
- Recording, session time limits, and modes without video playback require **Seamless reconnection** to be
  disabled.
- Closing the mirroring window stops the application, including reconnection attempts.
- OTG/AOA is unavailable in this portable build.
- V4L2 output requires Linux.
- Camera, codecs, and other device-specific capabilities depend on the phone. Changing an Advanced
  option cannot enable a capability that is unavailable on the phone or in this build.
- When a session is stopped, the launcher requests normal shutdown first so recordings can finish
  writing. If the native process does not exit within ten seconds, it is forcibly stopped, a warning
  is written to `app/last-run-errors.log`, and an active recording may be incomplete.

## Development and building

Start with [CONTRIBUTING.md](https://github.com/Nexusov/scrcpy-seamless/blob/main/CONTRIBUTING.md) for the test command and contribution workflow.

See the [documentation index](docs/README.md), [architecture](docs/ARCHITECTURE.md), [build guide](docs/BUILD.md), [packaging provenance](docs/PACKAGING.md), and [native implementation notes](docs/CHANGES.md).

The repository contains source code and launch scripts. Ready-to-run binaries are distributed
through [Releases](https://github.com/Nexusov/scrcpy-seamless/releases).

## License and third-party software

scrcpy Seamless is based on [Genymobile scrcpy 4.0](https://github.com/Genymobile/scrcpy/releases/tag/v4.0). The scrcpy code is licensed under Apache-2.0; bundled
third-party components retain their own licenses. scrcpy Seamless is an independent project and is
not an official Genymobile release.

See [LICENSE](LICENSE) for the Apache-2.0 license and [THIRD_PARTY.md](THIRD_PARTY.md) for component provenance and dependency
licensing information. Upstream copyright notices and licensing are preserved.

This software uses FFmpeg libraries under LGPL-2.1-or-later. Download the matching `scrcpy-seamless-dependency-sources.zip` from
the [same release](https://github.com/Nexusov/scrcpy-seamless/releases) as the portable package.

Third-party license notices are included in `app/licenses/` in the portable package and in `licenses/` in
the source repository.
