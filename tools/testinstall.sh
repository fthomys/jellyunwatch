#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'USAGE'
usage: tools/testinstall.sh <user@host>

Builds the plugin, uploads the package, unpacks it into the Jellyfin plugin
directory on that host, removes the archive and restarts the container.

environment:
  PLUGIN_ROOT    plugin directory on the host (default /var/lib/jellyfin/plugins)
  CONTAINER      docker container to restart (default jellyfin, empty to skip docker)
  OWNER          chown target for the installed files, for example 99:100 (default none)
  RESTART        1 to restart the container, 0 to skip (default 1)
  BUILD          1 to build before uploading, 0 to reuse the existing package (default 1)
  PURGE_LEGACY   1 to also remove jon4hz DiscontinueWatching_* folders (default 0)

Defaults can be stored in tools/testinstall.env, which is sourced when present.
USAGE
}

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [ -f "$ROOT/tools/testinstall.env" ]; then
    set -a
    . "$ROOT/tools/testinstall.env"
    set +a
fi

REMOTE="${1:-${REMOTE:-}}"

case "$REMOTE" in
    -h|--help)
        usage
        exit 0
        ;;
    "")
        usage
        exit 1
        ;;
esac

PLUGIN_ROOT="${PLUGIN_ROOT:-/var/lib/jellyfin/plugins}"
CONTAINER="${CONTAINER:-jellyfin}"
OWNER="${OWNER:-}"
RESTART="${RESTART:-1}"
BUILD="${BUILD:-1}"
PURGE_LEGACY="${PURGE_LEGACY:-0}"

NAME="$(grep -E '^name:' "$ROOT/build.yaml" | head -1 | sed -E 's/.*"(.*)".*/\1/')"
VERSION="$(grep -E '^version:' "$ROOT/build.yaml" | head -1 | sed -E 's/.*"(.*)".*/\1/')"
ZIP="$ROOT/bin/jelly-unwatch_$VERSION.zip"
TARGET="$PLUGIN_ROOT/${NAME}_$VERSION"

if [ "$BUILD" = "1" ]; then
    "$ROOT/build/package.sh"
fi

if [ ! -f "$ZIP" ]; then
    echo "package not found: $ZIP" >&2
    exit 1
fi

echo "uploading $(basename "$ZIP") to $REMOTE"
scp -q "$ZIP" "$REMOTE:/tmp/$(basename "$ZIP")"

ssh "$REMOTE" "NAME='$NAME' VERSION='$VERSION' PLUGIN_ROOT='$PLUGIN_ROOT' TARGET='$TARGET' \
    OWNER='$OWNER' CONTAINER='$CONTAINER' RESTART='$RESTART' PURGE_LEGACY='$PURGE_LEGACY' \
    ZIP_NAME='$(basename "$ZIP")' bash -s" <<'REMOTE_SCRIPT'
set -euo pipefail

ZIP="/tmp/$ZIP_NAME"

extract() {
    if command -v unzip >/dev/null 2>&1; then
        unzip -q -o "$1" -d "$2"
    elif command -v busybox >/dev/null 2>&1; then
        busybox unzip -q -o "$1" -d "$2"
    elif command -v python3 >/dev/null 2>&1; then
        python3 -m zipfile -e "$1" "$2"
    else
        echo "no tool available to extract $1" >&2
        exit 1
    fi
}

if [ ! -d "$PLUGIN_ROOT" ]; then
    echo "plugin directory does not exist: $PLUGIN_ROOT" >&2
    exit 1
fi

find "$PLUGIN_ROOT" -maxdepth 1 -type d -name "${NAME}_*" -exec rm -rf {} +

if [ "$PURGE_LEGACY" = "1" ]; then
    find "$PLUGIN_ROOT" -maxdepth 1 -type d -name "DiscontinueWatching_*" -print -exec rm -rf {} +
fi

mkdir -p "$TARGET"
extract "$ZIP" "$TARGET"
rm -f "$ZIP"

if [ -n "$OWNER" ]; then
    chown -R "$OWNER" "$TARGET"
fi

echo "installed:"
ls -l "$TARGET"

if ! ls -d "$PLUGIN_ROOT"/*[Ff]ile*[Tt]ransformation* >/dev/null 2>&1; then
    echo "note: File Transformation is not installed, the web button stays hidden until it is"
fi

if [ "$RESTART" = "1" ] && [ -n "$CONTAINER" ] && command -v docker >/dev/null 2>&1; then
    echo "restarting container $CONTAINER"
    docker restart "$CONTAINER" >/dev/null
    sleep 8
    docker logs --since 60s "$CONTAINER" 2>&1 | grep -iE "jellyunwatch|jelly-unwatch|filetransformation|file transformation" || echo "no plugin log lines yet"
fi
REMOTE_SCRIPT

echo "done: $TARGET"
