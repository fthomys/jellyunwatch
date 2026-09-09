#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="Jellyfin.Plugin.JellyUnwatch"
VERSION="$(grep -E '^version:' "$ROOT/build.yaml" | head -1 | sed -E 's/.*"(.*)".*/\1/')"
TARGET_ABI="$(grep -E '^targetAbi:' "$ROOT/build.yaml" | head -1 | sed -E 's/.*"(.*)".*/\1/')"
GUID="$(grep -E '^guid:' "$ROOT/build.yaml" | head -1 | sed -E 's/.*"(.*)".*/\1/')"
OUT="$ROOT/bin"
STAGE="$OUT/plugin"

rm -rf "$STAGE"
mkdir -p "$STAGE"

dotnet publish "$ROOT/$PROJECT/$PROJECT.csproj" -c Release -o "$OUT/publish" --nologo
cp "$OUT/publish/$PROJECT.dll" "$STAGE/"

cat > "$STAGE/meta.json" <<META
{
    "category": "General",
    "changelog": "",
    "description": "Adds a per user hidden list for the Continue Watching and Next Up rows. Hidden items keep their playback position.",
    "guid": "$GUID",
    "name": "JellyUnwatch",
    "overview": "Remove items from Continue Watching without losing playback progress.",
    "owner": "fthomys",
    "targetAbi": "$TARGET_ABI",
    "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
    "version": "$VERSION",
    "status": "Active",
    "autoUpdate": true,
    "imagePath": ""
}
META

ZIP="$OUT/jelly-unwatch_$VERSION.zip"
rm -f "$ZIP"
(cd "$STAGE" && zip -q -r "$ZIP" .)

echo "package: $ZIP"
echo "md5: $(md5sum "$ZIP" | cut -d' ' -f1)"
