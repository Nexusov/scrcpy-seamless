#!/usr/bin/env bash
set -euo pipefail

# Exercise Gradle's own locking and checksum failures in a disposable source copy.
: "${GITHUB_WORKSPACE:?}"
: "${RUNNER_TEMP:?}"
: "${JAVA_HOME_17_X64:?}"
: "${ANDROID_HOME:?}"

export JAVA_HOME="$JAVA_HOME_17_X64"
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export GRADLE_USER_HOME="$GITHUB_WORKSPACE/work/gradle-ci"

for dependency_state in \
    src/scrcpy/buildscript-gradle.lockfile \
    src/scrcpy/server/gradle.lockfile \
    src/scrcpy/gradle/verification-metadata.xml; do
    test -s "$dependency_state"
    git ls-files --error-unmatch "$dependency_state" >/dev/null
done

fixture_directory="$(mktemp -d "$RUNNER_TEMP/gradle-protection.XXXXXX")"
while IFS= read -r -d '' source_path; do
    mkdir -p "$fixture_directory/$(dirname "$source_path")"
    cp "$source_path" "$fixture_directory/$source_path"
done < <(git ls-files --cached --others --exclude-standard -z -- src/scrcpy)

fixture_project="$fixture_directory/src/scrcpy"
verification_metadata="$fixture_project/gradle/verification-metadata.xml"
cp "$verification_metadata" "$fixture_directory/approved-verification-metadata.xml"

python3 - "$verification_metadata" <<'PY'
from pathlib import Path
import sys
import xml.etree.ElementTree as ElementTree

metadata_path = Path(sys.argv[1])
verification_namespace = "https://schema.gradle.org/dependency-verification"
ElementTree.register_namespace("", verification_namespace)
ElementTree.register_namespace("xsi", "http://www.w3.org/2001/XMLSchema-instance")
document = ElementTree.parse(metadata_path)
components = document.findall(f".//{{{verification_namespace}}}component")
plugin = next(
    component
    for component in components
    if component.get("group") == "com.android.tools.build" and component.get("name") == "gradle"
)
plugin_jar = next(
    artifact
    for artifact in plugin.findall(f"{{{verification_namespace}}}artifact")
    if artifact.get("name", "").endswith(".jar")
)
checksum = plugin_jar.find(f"{{{verification_namespace}}}sha256")
if checksum is None or not checksum.get("value"):
    raise SystemExit("The approved Android Gradle plugin checksum is missing")

checksum.set("value", "0" * 64)
document.write(metadata_path, encoding="utf-8", xml_declaration=True)
PY

if (cd "$fixture_project" && bash ./gradlew help --offline --no-configuration-cache --no-daemon --dependency-verification=strict) >"$fixture_directory/checksum.log" 2>&1; then
    echo "Gradle accepted a changed approved artifact checksum" >&2
    exit 1
fi

if ! grep -qi 'dependency verification failed' "$fixture_directory/checksum.log"; then
    cat "$fixture_directory/checksum.log" >&2
    echo "Gradle failed for a reason other than checksum verification" >&2
    exit 1
fi

cp "$fixture_directory/approved-verification-metadata.xml" "$verification_metadata"
python3 - "$fixture_project/server/gradle.lockfile" <<'PY'
from pathlib import Path
import sys

lock_path = Path(sys.argv[1])
lock_state = lock_path.read_text(encoding="utf-8")
expected_entry = "junit:junit:4.13.2="
if lock_state.count(expected_entry) != 1:
    raise SystemExit("Expected JUnit lock entry is missing or duplicated")

lock_path.write_text(lock_state.replace(expected_entry, "junit:junit:0.0.0=", 1), encoding="utf-8")
PY

if (cd "$fixture_project" && bash ./gradlew :server:testDebugUnitTest --offline --no-configuration-cache --no-daemon --dependency-verification=strict) >"$fixture_directory/lock.log" 2>&1; then
    echo "Gradle accepted a changed resolved JUnit version" >&2
    exit 1
fi

if ! grep -Eq 'Dependency Locking|dependency lock|lock state' "$fixture_directory/lock.log"; then
    cat "$fixture_directory/lock.log" >&2
    echo "Gradle failed for a reason unrelated to dependency locking" >&2
    exit 1
fi

echo "Gradle rejected both the changed checksum and the changed locked version."
