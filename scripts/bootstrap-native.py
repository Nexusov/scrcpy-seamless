"""Restore the pinned Windows native-client toolchain into an ignored cache."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import venv
import zipfile


REPOSITORY_DIRECTORY = Path(__file__).resolve().parents[1]
TOOLCHAIN = json.loads((REPOSITORY_DIRECTORY / "scripts/native-toolchain.json").read_text(encoding="utf-8"))
RELEASE_MANIFEST = json.loads((REPOSITORY_DIRECTORY / "release-manifest.json").read_text(encoding="utf-8"))
FFMPEG_LIBRARIES = {
    "libavformat": ("62.12.101", "avformat-62"),
    "libavcodec": ("62.28.101", "avcodec-62"),
    "libavutil": ("60.26.101", "avutil-60"),
    "libswresample": ("6.3.101", "swresample-6"),
}


def file_sha256(path: Path) -> str:
    """Return the SHA-256 of one downloaded or reviewed input."""
    digest = hashlib.sha256()

    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)

    return digest.hexdigest()


def download_verified(package: dict[str, str], directory: Path) -> Path:
    """Download an exact artifact and reject any mismatching cached bytes."""
    destination = directory / package["filename"]
    expected_hash = package["sha256"]

    if destination.exists():
        if file_sha256(destination) != expected_hash:
            raise RuntimeError(f"Cached archive has the wrong SHA-256: {destination}")

        return destination

    temporary_path = destination.with_name(destination.name + ".partial")

    if temporary_path.exists():
        raise RuntimeError(f"Partial download requires manual inspection: {temporary_path}")

    try:
        with urllib.request.urlopen(package["url"], timeout=120) as response:
            with temporary_path.open("xb") as output:
                shutil.copyfileobj(response, output)

        if file_sha256(temporary_path) != expected_hash:
            raise RuntimeError(f"Downloaded archive has the wrong SHA-256: {destination}")

        temporary_path.replace(destination)
    except Exception:
        temporary_path.unlink(missing_ok=True)
        raise

    return destination


def ensure_clean_extraction(destination: Path, expected_hash: str) -> bool:
    """Skip only a previously completed extraction of the same archive."""
    marker = destination / ".archive-sha256"

    if marker.exists() and marker.read_text(encoding="ascii").strip() == expected_hash:
        return False

    if destination.exists():
        raise RuntimeError(f"Incomplete or different extraction needs manual inspection: {destination}")

    return True


def extract_zip(archive_path: Path, destination: Path, expected_hash: str) -> None:
    """Extract a verified ZIP without allowing paths outside its cache directory."""
    if not ensure_clean_extraction(destination, expected_hash):
        return

    destination.mkdir(parents=True)

    with zipfile.ZipFile(archive_path) as archive:
        for member in archive.infolist():
            member_path = (destination / member.filename).resolve()

            if not member_path.is_relative_to(destination.resolve()):
                raise RuntimeError(f"Archive contains an unsafe path: {member.filename}")

        archive.extractall(destination)

    (destination / ".archive-sha256").write_text(expected_hash, encoding="ascii")


def extract_ffmpeg(archive_path: Path, directory: Path, expected_hash: str) -> Path:
    """Extract FFmpeg source; its generated public headers are built below."""
    destination = directory / "ffmpeg-8.1.1"

    if not ensure_clean_extraction(destination, expected_hash):
        return destination

    with tarfile.open(archive_path) as archive:
        archive.extractall(directory, filter="data")

    (destination / ".archive-sha256").write_text(expected_hash, encoding="ascii")
    return destination


def extract_w64devkit(archive_path: Path, directory: Path, expected_hash: str) -> Path:
    """Unpack the pinned self-extracting GCC bundle into the ignored cache."""
    destination = directory / "w64devkit"

    if ensure_clean_extraction(destination, expected_hash):
        subprocess.run([str(archive_path), "-y", f"-o{directory}"], check=True)
        (destination / ".archive-sha256").write_text(expected_hash, encoding="ascii")

    return destination


def install_meson(archive_path: Path, directory: Path) -> Path:
    """Install the pinned wheel offline into a project-local Python venv."""
    environment_directory = directory / "python"
    meson_executable = environment_directory / "Scripts/meson.exe"

    if not environment_directory.exists():
        venv.EnvBuilder(with_pip=True).create(environment_directory)

    python_executable = environment_directory / "Scripts/python.exe"
    result = (
        subprocess.run([str(meson_executable), "--version"], capture_output=True, text=True)
        if meson_executable.exists()
        else None
    )

    if result is None or result.returncode != 0 or result.stdout.strip() != TOOLCHAIN["packages"]["meson"]["version"]:
        subprocess.run([str(python_executable), "-m", "pip", "install", "--no-index", "--no-deps", str(archive_path)], check=True)

    return meson_executable


def configure_ffmpeg_headers(source_directory: Path, toolchain_directory: Path) -> None:
    """Generate FFmpeg public configuration headers without building its DLLs."""
    marker = source_directory / ".native-headers-configured"

    if marker.exists():
        return

    (toolchain_directory / "tmp").mkdir(exist_ok=True)
    environment = os.environ.copy()
    environment["TMPDIR"] = "../tmp"
    shell = toolchain_directory / "w64devkit/bin/sh.exe"
    command = (
        'export PATH="../w64devkit/bin:$PATH"; '
        "./configure --target-os=mingw32 --arch=x86_64 --cc=gcc "
        "--disable-everything --disable-programs --disable-doc "
        "--disable-autodetect --disable-x86asm --disable-static --enable-shared"
    )
    subprocess.run([str(shell), "-c", command], cwd=source_directory, env=environment, check=True)
    marker.write_text("FFmpeg 8.1.1 headers configured for MinGW x64\n", encoding="ascii")


def prepare_runtime(toolchain_directory: Path) -> Path:
    """Overlay the exact reviewed ADB trio and verify every runtime file."""
    runtime_directory = toolchain_directory / "scrcpy-release/scrcpy-win64-v4.0"
    platform_tools = toolchain_directory / "platform-tools/platform-tools"

    for filename in ("adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll"):
        source = platform_tools / filename
        expected_hash = RELEASE_MANIFEST["RuntimeFiles"][filename]

        if file_sha256(source) != expected_hash:
            raise RuntimeError(f"Platform Tools input differs from release manifest: {filename}")

        shutil.copy2(source, runtime_directory / filename)

    for filename, expected_hash in RELEASE_MANIFEST["RuntimeFiles"].items():
        runtime_file = runtime_directory / filename

        if file_sha256(runtime_file) != expected_hash:
            raise RuntimeError(f"Runtime input differs from release manifest: {filename}")

    return runtime_directory


def write_build_configuration(toolchain_directory: Path, runtime_directory: Path, meson_executable: Path) -> Path:
    """Write machine-specific paths to an ignored local configuration file."""
    compiler_bin_directory = toolchain_directory / "w64devkit/bin"
    pkg_config_directory = toolchain_directory / "pkgconfig"
    pkg_config_directory.mkdir(exist_ok=True)

    for name, (version, library) in FFMPEG_LIBRARIES.items():
        lines = [
            f"Name: {name}",
            "Description: FFmpeg 8.1.1 headers linked to reviewed scrcpy runtime",
            f"Version: {version}",
            f'Libs: -L"{runtime_directory.as_posix()}" -l{library}',
            f'Cflags: -I"{(toolchain_directory / "ffmpeg-8.1.1").as_posix()}"',
        ]
        (pkg_config_directory / f"{name}.pc").write_text("\n".join(lines) + "\n", encoding="utf-8")

    sdl_headers = toolchain_directory / "sdl-release/SDL3-3.4.8/x86_64-w64-mingw32/include"
    sdl_lines = [
        "Name: sdl3",
        "Description: SDL 3.4.8 headers linked to reviewed scrcpy runtime",
        "Version: 3.4.8",
        f'Libs: -L"{runtime_directory.as_posix()}" -lSDL3',
        f'Cflags: -I"{sdl_headers.as_posix()}"',
    ]
    (pkg_config_directory / "sdl3.pc").write_text("\n".join(sdl_lines) + "\n", encoding="utf-8")
    configuration = {
        "compiler": str(compiler_bin_directory / "gcc.exe"),
        "archiver": str(compiler_bin_directory / "ar.exe"),
        "windres": str(compiler_bin_directory / "windres.exe"),
        "pkgConfig": str(compiler_bin_directory / "pkg-config.exe"),
        "meson": str(meson_executable),
        "ninja": str(toolchain_directory / "ninja-release/ninja.exe"),
        "pkgConfigDirectories": [str(pkg_config_directory)],
        "compilerBinDirectory": str(compiler_bin_directory),
    }
    configuration_path = toolchain_directory / "build.local.json"
    configuration_path.write_text(json.dumps(configuration, indent=2) + "\n", encoding="utf-8")
    return configuration_path


def main() -> None:
    """Restore verified inputs and print the exact build paths to use."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache-directory", type=Path, default=REPOSITORY_DIRECTORY / "work/native")
    arguments = parser.parse_args()

    if sys.platform != "win32":
        raise RuntimeError("This bootstrap is for the Windows x64 native-client build.")

    if platform.machine().lower() not in {"amd64", "x86_64"}:
        raise RuntimeError(f"Windows x64 is required; found {platform.machine()}.")

    actual_python = ".".join(str(part) for part in sys.version_info[:3])

    if actual_python != TOOLCHAIN["hostPython"]:
        raise RuntimeError(f"Python {TOOLCHAIN['hostPython']} is required; found {actual_python}.")

    toolchain_directory = arguments.cache_directory.resolve()
    downloads_directory = toolchain_directory / "downloads"
    downloads_directory.mkdir(parents=True, exist_ok=True)
    packages = TOOLCHAIN["packages"]
    archives = {name: download_verified(package, downloads_directory) for name, package in packages.items()}
    extract_w64devkit(archives["w64devkit"], toolchain_directory, packages["w64devkit"]["sha256"])
    extract_zip(archives["ninja"], toolchain_directory / "ninja-release", packages["ninja"]["sha256"])
    extract_zip(archives["sdl"], toolchain_directory / "sdl-release", packages["sdl"]["sha256"])
    extract_zip(archives["scrcpyRuntime"], toolchain_directory / "scrcpy-release", packages["scrcpyRuntime"]["sha256"])
    extract_zip(archives["platformTools"], toolchain_directory / "platform-tools", packages["platformTools"]["sha256"])
    ffmpeg_directory = extract_ffmpeg(archives["ffmpegSource"], toolchain_directory, packages["ffmpegSource"]["sha256"])
    configure_ffmpeg_headers(ffmpeg_directory, toolchain_directory)
    meson_executable = install_meson(archives["meson"], toolchain_directory)
    runtime_directory = prepare_runtime(toolchain_directory)
    configuration_path = write_build_configuration(toolchain_directory, runtime_directory, meson_executable)
    print(f"Native build configuration: {configuration_path}")
    print(f"Reviewed runtime directory: {runtime_directory}")


if __name__ == "__main__":
    main()
