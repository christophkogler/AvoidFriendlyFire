#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/rsync-to-rimworld-mods.sh RIMWORLD_MODS_DIR [MOD_NAME]

Deploy the mod files into an explicit RimWorld Mods directory.

Examples:
  scripts/rsync-to-rimworld-mods.sh "$HOME/.steam/steam/steamapps/common/RimWorld/Mods"
  scripts/rsync-to-rimworld-mods.sh /tmp/release-root/Mods AvoidFriendlyFire

When no destination is provided, this script does not sync files. It checks
common Steam locations for RimWorld Mods directories and prints suggestions.
EOF
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"

mod_name="${2:-AvoidFriendlyFire}"

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ $# -eq 0 ]]; then
  usage
  printf '\nNo destination provided; no files were synced.\n' >&2
  printf '\nLikely RimWorld Mods directories:\n' >&2
  candidate_roots=(
    "${HOME}/.steam/steam"
    "${HOME}/.local/share/Steam"
    "${HOME}/snap/steam/common/.local/share/Steam"
    "${HOME}/Library/Application Support/Steam"
  )
  found=0
  for candidate_root in "${candidate_roots[@]}"; do
    candidate_mods_dir="${candidate_root}/steamapps/common/RimWorld/Mods"
    if [[ -d "${candidate_mods_dir}" ]]; then
      printf '  %s\n' "${candidate_mods_dir}" >&2
      found=1
    fi
  done
  if [[ "${found}" -eq 0 ]]; then
    printf '  No common Steam RimWorld Mods directories found.\n' >&2
  fi
  printf '\nRun again with one of those Mods directories as the first argument.\n' >&2
  exit 2
fi

mods_dir="${1%/}"
destination="${mods_dir}/${mod_name}"

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
