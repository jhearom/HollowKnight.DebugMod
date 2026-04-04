#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

HOLLOW_KNIGHT_FOLDER="${HOLLOW_KNIGHT_FOLDER:-/codex/hollow_knight_analysis/hk_157811833/hollow_knight_Data/Managed}"
OUTPUT_DIRECTORY="${OUTPUT_DIRECTORY:-/tmp/debugmod_out_1578}"
BUNDLE_ROOT="${BUNDLE_ROOT:-/tmp/debugmod_bundle_1578}"
BUNDLE_ZIP="${BUNDLE_ZIP:-/tmp/DebugMod-1578-windows-bundle.zip}"
SHARE_PATH="${SHARE_PATH:-/mnt/hgfs/Share/DebugMod-1578-windows-bundle.zip}"
INSTALLER_SOURCE="${INSTALLER_SOURCE:-$ROOT_DIR/scripts/install_debugmod_bundle_1578.bat}"
INSTALLER_SHARE_PATH="${INSTALLER_SHARE_PATH:-/mnt/hgfs/Share/install_debugmod_bundle_1578.bat}"
DOTNET_CLI_HOME_DIR="${DOTNET_CLI_HOME_DIR:-/tmp/dotnet_home}"
NUGET_PACKAGES_DIR="${NUGET_PACKAGES_DIR:-/tmp/nuget}"
MIRROR_TO_SHARE=1

MANAGED_PAYLOAD_FILES=(
    Assembly-CSharp.dll
    Assembly-CSharp.xml
    MMHOOK_Assembly-CSharp.dll
    MMHOOK_PlayMaker.dll
    Mono.Cecil.dll
    MonoMod.RuntimeDetour.dll
    MonoMod.Utils.dll
    Newtonsoft.Json.dll
    README.md
    unityscenerepacker.dll
)

usage() {
    cat <<'EOF'
Usage: package_1578_windows_bundle.sh [--no-mirror]

Rebuild DebugMod against the local 1.5.78.11833 managed snapshot,
assemble a Windows install bundle, and optionally mirror both the bundle
and installer script to the shared folder.

Environment overrides:
  HOLLOW_KNIGHT_FOLDER
  OUTPUT_DIRECTORY
  BUNDLE_ROOT
  BUNDLE_ZIP
  SHARE_PATH
  INSTALLER_SOURCE
  INSTALLER_SHARE_PATH
  DOTNET_CLI_HOME_DIR
  NUGET_PACKAGES_DIR
EOF
}

log() {
    printf '[package-1578] %s\n' "$1"
}

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        printf 'Missing required command: %s\n' "$1" >&2
        exit 1
    fi
}

while (($# > 0)); do
    case "$1" in
        --no-mirror)
            MIRROR_TO_SHARE=0
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            printf 'Unknown argument: %s\n' "$1" >&2
            usage >&2
            exit 1
            ;;
    esac
done

require_command dotnet
require_command zip
require_command tee
require_command grep
require_command awk
require_command sed
require_command cp

if [[ ! -d "$HOLLOW_KNIGHT_FOLDER" ]]; then
    printf 'Missing Hollow Knight managed snapshot: %s\n' "$HOLLOW_KNIGHT_FOLDER" >&2
    exit 1
fi

if [[ ! -f "$INSTALLER_SOURCE" ]]; then
    printf 'Missing installer source: %s\n' "$INSTALLER_SOURCE" >&2
    exit 1
fi

for required_file in "${MANAGED_PAYLOAD_FILES[@]}"; do
    if [[ ! -f "$HOLLOW_KNIGHT_FOLDER/$required_file" ]]; then
        printf 'Missing managed payload file: %s\n' "$HOLLOW_KNIGHT_FOLDER/$required_file" >&2
        exit 1
    fi
done

build_log="$(mktemp)"
trap 'rm -f "$build_log"' EXIT

log "Repo root: $ROOT_DIR"
log "Using Hollow Knight managed snapshot: $HOLLOW_KNIGHT_FOLDER"
log "Build output directory: $OUTPUT_DIRECTORY"
log "Bundle zip path: $BUNDLE_ZIP"
if (( MIRROR_TO_SHARE )); then
    log "Mirror target: $SHARE_PATH"
    log "Installer mirror target: $INSTALLER_SHARE_PATH"
else
    log "Mirror target: disabled"
fi

log "Step 1/4: rebuilding DebugMod"
set +e
DOTNET_CLI_HOME="$DOTNET_CLI_HOME_DIR" \
NUGET_PACKAGES="$NUGET_PACKAGES_DIR" \
dotnet build "$ROOT_DIR/Source/DebugMod.csproj" \
    -t:Rebuild \
    -p:HollowKnightFolder="$HOLLOW_KNIGHT_FOLDER" \
    -p:OutputDirectory="$OUTPUT_DIRECTORY" \
    -v minimal | tee "$build_log"
build_status=${PIPESTATUS[0]}
set -e

if (( build_status != 0 )); then
    log "Build failed. See log above."
    exit "$build_status"
fi

warning_count="$(grep -Eo '[0-9]+ Warning\(s\)' "$build_log" | tail -n 1 | awk '{print $1}')"
error_count="$(grep -Eo '[0-9]+ Error\(s\)' "$build_log" | tail -n 1 | awk '{print $1}')"
warning_count="${warning_count:-unknown}"
error_count="${error_count:-unknown}"
assembly_info_file="$ROOT_DIR/Source/obj/Debug/DebugMod.AssemblyInfo.cs"
build_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
if [[ -f "$assembly_info_file" ]]; then
    metadata_build_utc="$(awk -F'\"' '/AssemblyMetadataAttribute\("BuildUtc"/ {print $4}' "$assembly_info_file" | tail -n 1)"
    if [[ -n "$metadata_build_utc" ]]; then
        build_utc="$metadata_build_utc"
    fi
fi

debugmod_dir="$OUTPUT_DIRECTORY/DebugMod"
for required_file in DebugMod.dll DebugMod.xml DebugMod.pdb; do
    if [[ ! -f "$debugmod_dir/$required_file" ]]; then
        printf 'Missing build artifact: %s\n' "$debugmod_dir/$required_file" >&2
        exit 1
    fi
done

log "Step 2/4: assembling bundle contents"
rm -rf "$BUNDLE_ROOT"
mkdir -p "$BUNDLE_ROOT/hollow_knight_Data/Managed/Mods/DebugMod"
for managed_file in "${MANAGED_PAYLOAD_FILES[@]}"; do
    cp -a "$HOLLOW_KNIGHT_FOLDER/$managed_file" "$BUNDLE_ROOT/hollow_knight_Data/Managed/"
done
cp -a \
    "$debugmod_dir/DebugMod.dll" \
    "$debugmod_dir/DebugMod.xml" \
    "$debugmod_dir/DebugMod.pdb" \
    "$BUNDLE_ROOT/hollow_knight_Data/Managed/Mods/DebugMod/"

cat > "$BUNDLE_ROOT/INSTALL.txt" <<'EOF'
Copy the contents of this archive into the Hollow Knight game root on Windows and overwrite existing files.

Expected target root example:
  ...\Steam\steamapps\common\Hollow Knight\

This bundle contains:
- 1.5.78.11833 managed payload files required for the modded install
- DebugMod build outputs under hollow_knight_Data\Managed\Mods\DebugMod
EOF

cat > "$BUNDLE_ROOT/BUILD_IDENTITY.txt" <<EOF
DebugMod bundle build identity

Patch: 1.5.78.11833
Build UTC: $build_utc
DebugMod DLL: hollow_knight_Data/Managed/Mods/DebugMod/DebugMod.dll

This same Build UTC should appear in DebugMod startup logging in:
- ModLog.txt
- Player.log
EOF

log "Step 3/4: creating zip archive"
rm -f "$BUNDLE_ZIP"
(
    cd "$(dirname "$BUNDLE_ROOT")"
    zip -qr "$BUNDLE_ZIP" "$(basename "$BUNDLE_ROOT")"
)

if [[ ! -f "$BUNDLE_ZIP" ]]; then
    printf 'Bundle zip was not created: %s\n' "$BUNDLE_ZIP" >&2
    exit 1
fi

if (( MIRROR_TO_SHARE )); then
    log "Step 4/4: mirroring bundle and installer to shared folder"
    mkdir -p "$(dirname "$SHARE_PATH")"
    mkdir -p "$(dirname "$INSTALLER_SHARE_PATH")"
    cp "$BUNDLE_ZIP" "$SHARE_PATH"
    cp "$INSTALLER_SOURCE" "$INSTALLER_SHARE_PATH"
    if [[ ! -f "$SHARE_PATH" ]]; then
        printf 'Mirror copy failed: %s\n' "$SHARE_PATH" >&2
        exit 1
    fi
    if [[ ! -f "$INSTALLER_SHARE_PATH" ]]; then
        printf 'Installer mirror copy failed: %s\n' "$INSTALLER_SHARE_PATH" >&2
        exit 1
    fi
else
    log "Step 4/4: mirror skipped by request"
fi

bundle_size="$(du -h "$BUNDLE_ZIP" | awk '{print $1}')"
log "Done"
printf '\nSummary\n'
printf '  Build errors   : %s\n' "$error_count"
printf '  Build warnings : %s\n' "$warning_count"
printf '  Build UTC      : %s\n' "$build_utc"
printf '  Bundle zip     : %s (%s)\n' "$BUNDLE_ZIP" "$bundle_size"
printf '  DebugMod dll   : %s\n' "$debugmod_dir/DebugMod.dll"
if (( MIRROR_TO_SHARE )); then
    printf '  Mirrored zip   : %s\n' "$SHARE_PATH"
    printf '  Mirrored bat   : %s\n' "$INSTALLER_SHARE_PATH"
else
    printf '  Mirrored copy  : skipped\n'
fi
