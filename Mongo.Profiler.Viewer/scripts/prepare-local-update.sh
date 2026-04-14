#!/usr/bin/env bash
set -euo pipefail

VERSION=""
RUNTIME=""
CHANNEL=""
PACK_ID="Mongo.Profiler.Viewer"
MAIN_EXE="Mongo.Profiler.Viewer"
FEED_DIR=""
CREATE_INSTALLER=false

usage() {
    cat <<EOF
Usage: $0 [options]

Options:
  -v, --version <ver>        Package version (default: from nbgv or generated metadata)
  -r, --runtime <rid>        Runtime identifier (default: auto-detect)
  -c, --channel <name>       Velopack channel (default: osx / linux)
      --pack-id <id>         Velopack pack id (default: Mongo.Profiler.Viewer)
      --main-exe <name>      Main executable name (default: Mongo.Profiler.Viewer)
      --feed-dir <path>      Local feed directory (default: \$HOME/mongo-profiler)
      --installer            Produce installer (default: off)
  -h, --help                 Show this help
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -v|--version) VERSION="$2"; shift 2 ;;
        -r|--runtime) RUNTIME="$2"; shift 2 ;;
        -c|--channel) CHANNEL="$2"; shift 2 ;;
        --pack-id) PACK_ID="$2"; shift 2 ;;
        --main-exe) MAIN_EXE="$2"; shift 2 ;;
        --feed-dir) FEED_DIR="$2"; shift 2 ;;
        --installer) CREATE_INSTALLER=true; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
    esac
done

detect_runtime() {
    local os arch
    case "$(uname -s)" in
        Darwin) os="osx" ;;
        Linux) os="linux" ;;
        *) echo "Unsupported OS: $(uname -s)" >&2; exit 1 ;;
    esac
    case "$(uname -m)" in
        x86_64|amd64) arch="x64" ;;
        arm64|aarch64) arch="arm64" ;;
        *) echo "Unsupported arch: $(uname -m)" >&2; exit 1 ;;
    esac
    echo "${os}-${arch}"
}

if [[ -z "$RUNTIME" ]]; then
    RUNTIME="$(detect_runtime)"
fi

if [[ -z "$CHANNEL" ]]; then
    CHANNEL="${RUNTIME%%-*}"
fi

if [[ -z "$FEED_DIR" ]]; then
    FEED_DIR="$HOME/mongo-profiler"
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet command not found." >&2
    exit 1
fi

if ! command -v vpk >/dev/null 2>&1; then
    echo "vpk command not found. Install it with: dotnet tool install -g vpk" >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VIEWER_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_PATH="$VIEWER_DIR/Mongo.Profiler.Viewer.csproj"
ARTIFACTS_ROOT="$VIEWER_DIR/artifacts"

resolve_version_from_nbgv() {
    if ! command -v nbgv >/dev/null 2>&1; then return; fi
    ( cd "$VIEWER_DIR" && nbgv get-version -v NuGetPackageVersion 2>/dev/null ) | tr -d '[:space:]'
}

resolve_version_from_metadata() {
    dotnet build "$PROJECT_PATH" -c Release -p:RestoreIgnoreFailedSources=true >/dev/null
    local version_file="$VIEWER_DIR/obj/Release/net10.0/Mongo.Profiler.Viewer.Version.cs"
    if [[ -f "$version_file" ]]; then
        local v
        v="$(sed -n 's/.*NuGetPackageVersion = "\([^"]*\)".*/\1/p' "$version_file" | head -n 1)"
        if [[ -z "$v" ]]; then
            v="$(sed -n 's/.*AssemblyFileVersion = "\([^"]*\)".*/\1/p' "$version_file" | head -n 1)"
        fi
        echo "$v"
    fi
}

if [[ -z "$VERSION" ]]; then
    VERSION="$(resolve_version_from_nbgv || true)"
fi
if [[ -z "$VERSION" ]]; then
    VERSION="$(resolve_version_from_metadata || true)"
fi
if [[ -z "$VERSION" ]]; then
    VERSION="0.0.$(date +%s)"
fi

PUBLISH_DIR="$ARTIFACTS_ROOT/publish/$RUNTIME/$VERSION"
RELEASE_DIR="$ARTIFACTS_ROOT/releases/$RUNTIME/$VERSION"

echo "Preparing local viewer update package..."
echo "Version : $VERSION"
echo "Runtime : $RUNTIME"
echo "Channel : $CHANNEL"
echo "Feed    : $FEED_DIR"

mkdir -p "$PUBLISH_DIR" "$RELEASE_DIR" "$FEED_DIR"

dotnet publish "$PROJECT_PATH" -c Release --self-contained -r "$RUNTIME" -p:RestoreIgnoreFailedSources=true -o "$PUBLISH_DIR"

MAIN_EXE_PATH="$PUBLISH_DIR/$MAIN_EXE"
if [[ ! -f "$MAIN_EXE_PATH" ]]; then
    echo "Publish completed but main executable '$MAIN_EXE' was not found in '$PUBLISH_DIR'." >&2
    echo "Executables present:" >&2
    find "$PUBLISH_DIR" -maxdepth 1 -type f -perm -u+x -exec basename {} \; >&2 || true
    exit 1
fi

VPK_ARGS=(pack
    --packId "$PACK_ID"
    --packVersion "$VERSION"
    --packDir "$PUBLISH_DIR"
    --mainExe "$MAIN_EXE"
    --channel "$CHANNEL"
    --outputDir "$RELEASE_DIR"
)
if [[ "$CREATE_INSTALLER" != "true" ]]; then
    VPK_ARGS+=(--noInst)
fi

vpk "${VPK_ARGS[@]}"

if ! find "$RELEASE_DIR" -maxdepth 1 -type f | read -r _; then
    echo "No release files were generated in '$RELEASE_DIR'." >&2
    exit 1
fi

vpk upload local \
    --outputDir "$RELEASE_DIR" \
    --channel "$CHANNEL" \
    --path "$FEED_DIR" \
    --regenerate

echo
echo "Local feed ready:"
echo "  $FEED_DIR"
echo
echo "Set these variables before launching installed viewer:"
echo "  export MONGO_PROFILER_VIEWER_UPDATE_FEED_URL='$FEED_DIR'"
echo "  export MONGO_PROFILER_VIEWER_UPDATE_CHANNEL='$CHANNEL'"
