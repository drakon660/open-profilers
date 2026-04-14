#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="Release"
RUNTIME=""
OUTPUT_DIR=""
SELF_CONTAINED=true

usage() {
    cat <<EOF
Usage: $0 [options]

Options:
  -c, --configuration <name>   Build configuration (default: Release)
  -r, --runtime <rid>          Target runtime (default: auto-detect: osx-<arch> / linux-<arch>)
  -o, --output <dir>           Output directory (default: artifacts/publish/<runtime>/<version>)
      --no-self-contained      Publish as framework-dependent (default: self-contained)
  -h, --help                   Show this help
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -c|--configuration) CONFIGURATION="$2"; shift 2 ;;
        -r|--runtime) RUNTIME="$2"; shift 2 ;;
        -o|--output) OUTPUT_DIR="$2"; shift 2 ;;
        --self-contained) SELF_CONTAINED=true; shift ;;
        --no-self-contained) SELF_CONTAINED=false; shift ;;
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

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet command not found." >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VIEWER_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_PATH="$VIEWER_DIR/Mongo.Profiler.Viewer.csproj"
ARTIFACTS_ROOT="$VIEWER_DIR/artifacts"

resolve_version() {
    dotnet build "$PROJECT_PATH" -c "$CONFIGURATION" -p:RestoreIgnoreFailedSources=true >/dev/null
    local version_file="$VIEWER_DIR/obj/$CONFIGURATION/net10.0/Mongo.Profiler.Viewer.Version.cs"
    if [[ -f "$version_file" ]]; then
        sed -n 's/.*NuGetPackageVersion = "\([^"]*\)".*/\1/p' "$version_file" | head -n 1
    fi
}

VERSION="$(resolve_version)"

if [[ -z "$OUTPUT_DIR" ]]; then
    VERSION_SEGMENT="${VERSION:-unknown-version}"
    OUTPUT_DIR="$ARTIFACTS_ROOT/publish/$RUNTIME/$VERSION_SEGMENT"
fi

echo "Building viewer..."
echo "Configuration : $CONFIGURATION"
echo "Runtime       : $RUNTIME"
echo "Version       : $VERSION"
echo "Output        : $OUTPUT_DIR"

mkdir -p "$OUTPUT_DIR"

PUBLISH_ARGS=(publish "$PROJECT_PATH" -c "$CONFIGURATION" -r "$RUNTIME" -p:RestoreIgnoreFailedSources=true -o "$OUTPUT_DIR")
if [[ "$SELF_CONTAINED" == "true" ]]; then
    PUBLISH_ARGS+=(--self-contained)
else
    PUBLISH_ARGS+=(--no-self-contained)
fi

dotnet "${PUBLISH_ARGS[@]}"

echo
echo "Viewer build ready:"
echo "  $OUTPUT_DIR"
