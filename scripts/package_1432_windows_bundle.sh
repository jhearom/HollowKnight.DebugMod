#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

HOLLOW_KNIGHT_FOLDER="${HOLLOW_KNIGHT_FOLDER:-/codex/hollow_knight_analysis/hk_1432/hollow_knight_Data/Managed}"
MODDING_API_SOLUTION="${MODDING_API_SOLUTION:-/codex/ModdingAPI/HollowKnight.Modding.API.sln}"
PATCHED_ASSEMBLY_PATH="${PATCHED_ASSEMBLY_PATH:-/codex/ModdingAPI/OutputFinal/hollow_knight_Data/Managed/Assembly-CSharp.dll}"
PATCHED_ASSEMBLY_XML_PATH="${PATCHED_ASSEMBLY_XML_PATH:-/codex/ModdingAPI/OutputFinal/hollow_knight_Data/Managed/Assembly-CSharp.xml}"
HOOK_ASSEMBLY_PATH="${HOOK_ASSEMBLY_PATH:-/codex/ModdingAPI/Assembly-CSharp/bin/Debug/net35/MMHOOK_Assembly-CSharp.dll}"
PLAYMAKER_HOOK_PATH="${PLAYMAKER_HOOK_PATH:-/codex/ModdingAPI/Assembly-CSharp/bin/Debug/net35/MMHOOK_PlayMaker.dll}"
NETSTANDARD_PATH="${NETSTANDARD_PATH:-/usr/lib/mono/4.7.2-api/Facades/netstandard.dll}"
FRAMEWORK_PATH_OVERRIDE="${FRAMEWORK_PATH_OVERRIDE:-/usr/lib/mono/2.0-api}"
SUPPORT_SOURCE_DIR="${SUPPORT_SOURCE_DIR:-/codex/ModdingAPI/Assembly-CSharp/bin/Debug/net35}"
OUTPUT_DIRECTORY="${OUTPUT_DIRECTORY:-/tmp/debugmod_out_1432}"
BUNDLE_ROOT="${BUNDLE_ROOT:-/tmp/debugmod_bundle_1432}"
BUNDLE_ZIP="${BUNDLE_ZIP:-/tmp/DebugMod-1432-windows-bundle.zip}"
SHARE_PATH="${SHARE_PATH:-/mnt/hgfs/Share/DebugMod-1432-windows-bundle.zip}"
DOTNET_CLI_HOME_DIR="${DOTNET_CLI_HOME_DIR:-/tmp/dotnet_home}"
NUGET_PACKAGES_DIR="${NUGET_PACKAGES_DIR:-/tmp/nuget}"
MIRROR_TO_SHARE=1

usage() {
    cat <<'EOF'
Usage: package_1432_windows_bundle.sh [--no-mirror]

Rebuild DebugMod against the current 1.4.3.2 managed refs, assemble a Windows
install bundle, and optionally mirror it to the shared folder.

Environment overrides:
  HOLLOW_KNIGHT_FOLDER
  MODDING_API_SOLUTION
  PATCHED_ASSEMBLY_PATH
  PATCHED_ASSEMBLY_XML_PATH
  HOOK_ASSEMBLY_PATH
  PLAYMAKER_HOOK_PATH
  NETSTANDARD_PATH
  FRAMEWORK_PATH_OVERRIDE
  SUPPORT_SOURCE_DIR
  OUTPUT_DIRECTORY
  BUNDLE_ROOT
  BUNDLE_ZIP
  SHARE_PATH
  DOTNET_CLI_HOME_DIR
  NUGET_PACKAGES_DIR
EOF
}

log() {
    printf '[package-1432] %s\n' "$1"
}

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        printf 'Missing required command: %s\n' "$1" >&2
        exit 1
    fi
}

copy_required_file() {
    local source_dir="$1"
    local file_name="$2"
    local destination_dir="$3"

    if [[ ! -f "$source_dir/$file_name" ]]; then
        printf 'Missing required support file: %s\n' "$source_dir/$file_name" >&2
        exit 1
    fi

    cp -a "$source_dir/$file_name" "$destination_dir/"
}

copy_optional_file() {
    local source_path="$1"
    local destination_dir="$2"

    if [[ -f "$source_path" ]]; then
        cp -a "$source_path" "$destination_dir/"
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

if [[ ! -d "$HOLLOW_KNIGHT_FOLDER" ]]; then
    printf 'Missing Hollow Knight managed snapshot: %s\n' "$HOLLOW_KNIGHT_FOLDER" >&2
    exit 1
fi

for required_path in \
    "$MODDING_API_SOLUTION" \
    "$PATCHED_ASSEMBLY_PATH" \
    "$HOOK_ASSEMBLY_PATH" \
    "$PLAYMAKER_HOOK_PATH" \
    "$NETSTANDARD_PATH"; do
    if [[ ! -e "$required_path" ]]; then
        printf 'Missing required file: %s\n' "$required_path" >&2
        exit 1
    fi
done

if [[ ! -d "$SUPPORT_SOURCE_DIR" ]]; then
    printf 'Missing support source directory: %s\n' "$SUPPORT_SOURCE_DIR" >&2
    exit 1
fi

build_log="$(mktemp)"
trap 'rm -f "$build_log"' EXIT

log "Repo root: $ROOT_DIR"
log "Using Hollow Knight managed snapshot: $HOLLOW_KNIGHT_FOLDER"
log "Using Modding API solution: $MODDING_API_SOLUTION"
log "Using patched Assembly-CSharp: $PATCHED_ASSEMBLY_PATH"
log "Using hook assembly: $HOOK_ASSEMBLY_PATH"
log "Using PlayMaker hook assembly: $PLAYMAKER_HOOK_PATH"
log "Using support source dir: $SUPPORT_SOURCE_DIR"
log "Build output directory: $OUTPUT_DIRECTORY"
log "Bundle zip path: $BUNDLE_ZIP"
if (( MIRROR_TO_SHARE )); then
    log "Mirror target: $SHARE_PATH"
else
    log "Mirror target: disabled"
fi

log "Step 1/5: rebuilding ModdingAPI"
set +e
DOTNET_CLI_HOME="$DOTNET_CLI_HOME_DIR" \
NUGET_PACKAGES="$NUGET_PACKAGES_DIR" \
dotnet build "$MODDING_API_SOLUTION" \
    -c Debug \
    -v minimal | tee "$build_log"
build_status=${PIPESTATUS[0]}
set -e

if (( build_status != 0 )); then
    log "ModdingAPI build failed. See log above."
    exit "$build_status"
fi

log "Step 2/5: rebuilding DebugMod"
build_log="$(mktemp)"
trap 'rm -f "$build_log"' EXIT
set +e
DOTNET_CLI_HOME="$DOTNET_CLI_HOME_DIR" \
NUGET_PACKAGES="$NUGET_PACKAGES_DIR" \
dotnet msbuild "$ROOT_DIR/Source/DebugMod.csproj" \
    /t:Rebuild \
    /p:HollowKnightFolder="$HOLLOW_KNIGHT_FOLDER" \
    /p:AssemblyCSharpPath="$PATCHED_ASSEMBLY_PATH" \
    /p:HookAssemblyPath="$HOOK_ASSEMBLY_PATH" \
    /p:NetStandardPath="$NETSTANDARD_PATH" \
    /p:FrameworkPathOverride="$FRAMEWORK_PATH_OVERRIDE" \
    /p:OutputDirectory="$OUTPUT_DIRECTORY" \
    /verbosity:minimal | tee "$build_log"
build_status=${PIPESTATUS[0]}
set -e

if (( build_status != 0 )); then
    log "Build failed. See log above."
    exit "$build_status"
fi

warning_count="$(grep -c ': warning ' "$build_log" || true)"
error_count="$(grep -c ': error ' "$build_log" || true)"
warning_count="${warning_count:-unknown}"
error_count="${error_count:-unknown}"
build_info_file="$ROOT_DIR/Source/obj/Debug/BuildInfo.g.cs"
build_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
if [[ -f "$build_info_file" ]]; then
    build_utc="$(awk -F'\"' '/BuildUtc = / {print $2}' "$build_info_file" | tail -n 1)"
    build_utc="${build_utc:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}"
fi

debugmod_dir="$OUTPUT_DIRECTORY/DebugMod"
if [[ ! -f "$debugmod_dir/DebugMod.dll" ]]; then
    printf 'Missing build artifact: %s\n' "$debugmod_dir/DebugMod.dll" >&2
    exit 1
fi

log "Step 3/5: assembling bundle contents"
rm -rf "$BUNDLE_ROOT"
mkdir -p "$BUNDLE_ROOT/hollow_knight_Data/Managed/Mods"
mkdir -p "$BUNDLE_ROOT/hollow_knight_Data/Managed"

copy_required_file "$(dirname "$PATCHED_ASSEMBLY_PATH")" "$(basename "$PATCHED_ASSEMBLY_PATH")" "$BUNDLE_ROOT/hollow_knight_Data/Managed"
copy_required_file "$(dirname "$HOOK_ASSEMBLY_PATH")" "$(basename "$HOOK_ASSEMBLY_PATH")" "$BUNDLE_ROOT/hollow_knight_Data/Managed"
copy_required_file "$(dirname "$PLAYMAKER_HOOK_PATH")" "$(basename "$PLAYMAKER_HOOK_PATH")" "$BUNDLE_ROOT/hollow_knight_Data/Managed"
copy_required_file "$SUPPORT_SOURCE_DIR" "Mono.Cecil.dll" "$BUNDLE_ROOT/hollow_knight_Data/Managed"
copy_required_file "$SUPPORT_SOURCE_DIR" "MonoMod.RuntimeDetour.dll" "$BUNDLE_ROOT/hollow_knight_Data/Managed"
copy_required_file "$SUPPORT_SOURCE_DIR" "MonoMod.Utils.dll" "$BUNDLE_ROOT/hollow_knight_Data/Managed"
copy_required_file "$SUPPORT_SOURCE_DIR" "Newtonsoft.Json.dll" "$BUNDLE_ROOT/hollow_knight_Data/Managed"
copy_required_file "$SUPPORT_SOURCE_DIR" "unityscenerepacker.dll" "$BUNDLE_ROOT/hollow_knight_Data/Managed"
copy_required_file "$SUPPORT_SOURCE_DIR" "README.md" "$BUNDLE_ROOT/hollow_knight_Data/Managed"
copy_required_file "$SUPPORT_SOURCE_DIR" "BUILD_INFO.txt" "$BUNDLE_ROOT/hollow_knight_Data/Managed"

cp -a "$debugmod_dir/DebugMod.dll" "$BUNDLE_ROOT/hollow_knight_Data/Managed/Mods/"
copy_optional_file "$debugmod_dir/DebugMod.pdb" "$BUNDLE_ROOT/hollow_knight_Data/Managed/Mods"
copy_optional_file "$debugmod_dir/DebugMod.xml" "$BUNDLE_ROOT/hollow_knight_Data/Managed/Mods"

copy_optional_file "$PATCHED_ASSEMBLY_XML_PATH" "$BUNDLE_ROOT/hollow_knight_Data/Managed"

cat > "$BUNDLE_ROOT/INSTALL.txt" <<'EOF'
Copy the contents of this archive into the Hollow Knight game root on Windows and overwrite existing files.

Expected target root example:
  ...\Steam\steamapps\common\Hollow Knight\

This bundle contains:
- patched `Assembly-CSharp.dll` for ModdingAPI on `1.4.3.2`
- DebugMod under hollow_knight_Data\Managed\Mods
- ModdingAPI hook/support assemblies needed for the clean `1.4.3.2` install
EOF

cat > "$BUNDLE_ROOT/BUILD_IDENTITY.txt" <<EOF
DebugMod bundle build identity

Patch scope: 1.4.3.2
Build UTC: $build_utc
Patched game assembly: hollow_knight_Data/Managed/$(basename "$PATCHED_ASSEMBLY_PATH")
DebugMod DLL: hollow_knight_Data/Managed/Mods/DebugMod.dll
Hook assembly: hollow_knight_Data/Managed/$(basename "$HOOK_ASSEMBLY_PATH")
PlayMaker hook assembly: hollow_knight_Data/Managed/$(basename "$PLAYMAKER_HOOK_PATH")

This same Build UTC should appear in DebugMod startup logging in:
- ModLog.txt
- Player.log
EOF

log "Step 4/5: creating zip archive"
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
    log "Step 5/5: mirroring bundle to shared folder"
    mkdir -p "$(dirname "$SHARE_PATH")"
    cp "$BUNDLE_ZIP" "$SHARE_PATH"
    if [[ ! -f "$SHARE_PATH" ]]; then
        printf 'Mirror copy failed: %s\n' "$SHARE_PATH" >&2
        exit 1
    fi
else
    log "Step 5/5: mirror skipped by request"
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
    printf '  Mirrored copy  : %s\n' "$SHARE_PATH"
else
    printf '  Mirrored copy  : skipped\n'
fi
