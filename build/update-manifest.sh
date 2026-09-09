#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO="${REPO:-fthomys/jelly-unwatch}"
SERVER="${SERVER:-https://github.com}"
VERSION="$(grep -E '^version:' "$ROOT/build.yaml" | head -1 | sed -E 's/.*"(.*)".*/\1/')"
TAG="${TAG:-v$VERSION}"
ZIP="${ZIP:-$ROOT/bin/jelly-unwatch_$VERSION.zip}"

if [ ! -f "$ZIP" ]; then
    echo "package not found: $ZIP" >&2
    exit 1
fi

CHECKSUM="$(md5sum "$ZIP" | cut -d' ' -f1)"
SOURCE_URL="$SERVER/$REPO/releases/download/$TAG/$(basename "$ZIP")"

MANIFEST="$ROOT/manifest.json" \
BUILD_YAML="$ROOT/build.yaml" \
VERSION="$VERSION" \
CHECKSUM="$CHECKSUM" \
SOURCE_URL="$SOURCE_URL" \
python3 <<'PY'
import json
import os
import re
from datetime import datetime, timezone

manifest_path = os.environ['MANIFEST']
build_yaml = open(os.environ['BUILD_YAML'], encoding='utf-8').read()


def field(name):
    match = re.search(r'^%s:\s*"(.*)"\s*$' % name, build_yaml, re.MULTILINE)
    return match.group(1) if match else ''


def block(name):
    match = re.search(r'^%s:\s*>\n((?:[ ]+.*\n)+)' % name, build_yaml, re.MULTILINE)
    if not match:
        return field(name)
    return ' '.join(line.strip() for line in match.group(1).splitlines() if line.strip())


version = os.environ['VERSION']

entry = {
    'version': version,
    'changelog': field('changelog'),
    'targetAbi': field('targetAbi'),
    'sourceUrl': os.environ['SOURCE_URL'],
    'checksum': os.environ['CHECKSUM'],
    'timestamp': datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
}

try:
    manifest = json.load(open(manifest_path, encoding='utf-8'))
except (OSError, ValueError):
    manifest = []

plugin = next((p for p in manifest if p.get('guid') == field('guid')), None)
if plugin is None:
    plugin = {'guid': field('guid'), 'versions': []}
    manifest.append(plugin)

plugin['name'] = field('name')
plugin['description'] = block('description')
plugin['overview'] = field('overview')
plugin['owner'] = field('owner')
plugin['category'] = field('category')
plugin.setdefault('imageUrl', '')

versions = [v for v in plugin.get('versions', []) if v.get('version') != version]
versions.insert(0, entry)
plugin['versions'] = versions

with open(manifest_path, 'w', encoding='utf-8') as handle:
    json.dump(manifest, handle, indent=4)
    handle.write('\n')

print('manifest entry: %s -> %s' % (version, entry['sourceUrl']))
PY
