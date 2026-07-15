#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEFAULT_DEST="/mnt/c/Users/element/Documents/Klei/OxygenNotIncluded/mods/Local/ArknightsOperatorsMod"
DEST="${ONI_LOCAL_MOD_DIR:-$DEFAULT_DEST}"
LEGACY_DEST="/mnt/c/Users/element/Documents/Klei/OxygenNotIncluded/mods/Local/AmiyaDuplicantMod"
CONFIG_ROOT="/mnt/c/Users/element/Documents/Klei/OxygenNotIncluded/mods/config"
LEGACY_CONFIG="$CONFIG_ROOT/AmiyaDuplicantMod"
CURRENT_CONFIG="$CONFIG_ROOT/ArknightsOperatorsMod"

if [[ ! -f "$ROOT/ArknightsOperatorsMod.dll" ]]; then
	echo "Build the DLL first: bash build.sh" >&2
	exit 1
fi

if [[ "$DEST" == "$DEFAULT_DEST" && -d "$LEGACY_DEST" && -e "$DEST" ]]; then
	echo "Both legacy and current local Mod directories exist; resolve them before installing:" >&2
	echo "  $LEGACY_DEST" >&2
	echo "  $DEST" >&2
	exit 1
fi

if [[ "$DEST" == "$DEFAULT_DEST" && -d "$LEGACY_DEST" && ! -e "$DEST" ]]; then
	mv "$LEGACY_DEST" "$DEST"
	echo "Migrated legacy local Mod directory to $DEST"
fi

if [[ "$DEST" == "$DEFAULT_DEST" && -d "$LEGACY_CONFIG" && ! -e "$CURRENT_CONFIG" ]]; then
	mv "$LEGACY_CONFIG" "$CURRENT_CONFIG"
	echo "Migrated legacy configuration and cache to $CURRENT_CONFIG"
elif [[ "$DEST" == "$DEFAULT_DEST" && -d "$LEGACY_CONFIG" && -e "$CURRENT_CONFIG" ]]; then
	echo "Legacy and current configuration directories both exist; keeping the current directory and leaving the legacy directory untouched." >&2
fi

mkdir -p "$DEST"
cp "$ROOT/ArknightsOperatorsMod.dll" "$DEST/"
rm -f "$DEST/AmiyaDuplicantMod.dll"
cp "$ROOT/lib/PLib.dll" "$DEST/"
cp "$ROOT/mod.yaml" "$DEST/"
cp "$ROOT/mod_info.yaml" "$DEST/"
cp "$ROOT/PLIB-LICENSE.txt" "$DEST/"
cp "$ROOT/PLIB-SOURCE.txt" "$DEST/"
cp "$ROOT/SPINE-RUNTIME-LICENSE.txt" "$DEST/"
cp "$ROOT/lib/SPINE-RUNTIME-SOURCE.txt" "$DEST/"
rm -rf "$DEST/assets"
mkdir -p "$DEST/assets"
cp -R "$ROOT/assets/catalog" "$DEST/assets/catalog"
if [[ -d "$ROOT/assets/spine" ]]; then
  cp -R "$ROOT/assets/spine" "$DEST/assets/spine"
fi

echo "Installed to $DEST"
