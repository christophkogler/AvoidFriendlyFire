#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"

default_rimworld_root="/home/christoph/snap/steam/common/.local/share/Steam/steamapps/common/RimWorld"
rimworld_root="${1:-$default_rimworld_root}"
mod_name="${2:-AvoidFriendlyFire}"
destination="${rimworld_root%/}/Mods/${mod_name}"

mkdir -p "$destination"

rsync -av --delete \
  --include='/About/***' \
  --include='/Languages/***' \
  --include='/Textures/***' \
  --include='/1.6/' \
  --include='/1.6/Assemblies/' \
  --include='/1.6/Assemblies/AvoidFriendlyFire.dll' \
  --exclude='*' \
  "${repo_root}/" \
  "${destination}/"

printf 'Synced mod files to %s\n' "$destination"
