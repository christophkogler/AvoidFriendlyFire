#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/make-release.sh [options]

Bump the mod version, package the release zip, commit the version bump, and tag it.

Options:
  --bump patch|minor|major  Version component to increment (default: patch)
  --version X.Y.Z           Set an explicit semantic version
  --output-dir DIR          Write the release zip into DIR (default: repo root/dist)
  --allow-dirty             Skip the clean-worktree check
  --dry-run                 Print actions without changing files, creating commits, or tags
  -h, --help                Show this help text

Notes:
  - The assembly version written to AssemblyInfo.cs is X.Y.Z.0.
  - The git tag created is vX.Y.Z.
  - The release zip contains the same files targeted by scripts/rsync-to-rimworld-mods.sh.
EOF
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
assembly_info="${repo_root}/src/AvoidFriendlyFire/Properties/AssemblyInfo.cs"
rsync_script="${repo_root}/scripts/rsync-to-rimworld-mods.sh"
default_output_dir="${repo_root}/dist"
mod_name="AvoidFriendlyFire"

bump_part="patch"
explicit_version=""
output_dir="${default_output_dir}"
allow_dirty=0
dry_run=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --bump)
      [[ $# -ge 2 ]] || { echo "--bump requires a value" >&2; exit 1; }
      bump_part="$2"
      shift 2
      ;;
    --version)
      [[ $# -ge 2 ]] || { echo "--version requires a value" >&2; exit 1; }
      explicit_version="$2"
      shift 2
      ;;
    --output-dir)
      [[ $# -ge 2 ]] || { echo "--output-dir requires a value" >&2; exit 1; }
      output_dir="$2"
      shift 2
      ;;
    --allow-dirty)
      allow_dirty=1
      shift
      ;;
    --dry-run)
      dry_run=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -n "${explicit_version}" && "${bump_part}" != "patch" ]]; then
  echo "--version cannot be combined with --bump" >&2
  exit 1
fi

if [[ ! -f "${assembly_info}" ]]; then
  echo "Missing AssemblyInfo.cs at ${assembly_info}" >&2
  exit 1
fi

if [[ ! -x "${rsync_script}" ]]; then
  echo "Missing executable rsync script at ${rsync_script}" >&2
  exit 1
fi

assembly_dir="${repo_root}/1.6/Assemblies"
assembly_dll="${assembly_dir}/AvoidFriendlyFire.dll"
assembly_pdb="${assembly_dir}/AvoidFriendlyFire.pdb"

if [[ ! -f "${assembly_dll}" ]]; then
  echo "Missing build artifact ${assembly_dll}" >&2
  echo "Build the mod before making a release." >&2
  exit 1
fi

if [[ ! -f "${assembly_pdb}" ]]; then
  echo "Missing debug symbols ${assembly_pdb}" >&2
  echo "Build the mod before making a release." >&2
  exit 1
fi

if ! command -v zip >/dev/null 2>&1; then
  echo "zip is required but not installed" >&2
  exit 1
fi

if ! command -v git >/dev/null 2>&1; then
  echo "git is required but not installed" >&2
  exit 1
fi

if [[ "${allow_dirty}" -eq 0 ]]; then
  if [[ -n "$(git -C "${repo_root}" status --short)" ]]; then
    echo "Git worktree is not clean. Commit or stash changes first, or rerun with --allow-dirty." >&2
    exit 1
  fi
fi

current_version="$(sed -nE 's/^\[assembly: AssemblyVersion\("([0-9]+\.[0-9]+\.[0-9]+)\.0"\)\]/\1/p' "${assembly_info}")"
if [[ -z "${current_version}" ]]; then
  echo "Unable to parse semantic version from ${assembly_info}" >&2
  exit 1
fi

IFS='.' read -r current_major current_minor current_patch <<< "${current_version}"

if [[ -n "${explicit_version}" ]]; then
  if [[ ! "${explicit_version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Explicit version must match X.Y.Z" >&2
    exit 1
  fi
  next_version="${explicit_version}"
else
  case "${bump_part}" in
    patch)
      next_version="${current_major}.${current_minor}.$((current_patch + 1))"
      ;;
    minor)
      next_version="${current_major}.$((current_minor + 1)).0"
      ;;
    major)
      next_version="$((current_major + 1)).0.0"
      ;;
    *)
      echo "Invalid bump part: ${bump_part}" >&2
      exit 1
      ;;
  esac
fi

if [[ "${next_version}" == "${current_version}" ]]; then
  echo "Next version matches current version (${current_version})" >&2
  exit 1
fi

tag_name="v${next_version}"
archive_name="${mod_name}-${tag_name}.zip"
temp_root="$(mktemp -d)"
cleanup() {
  rm -rf "${temp_root}"
}
trap cleanup EXIT

if [[ "${output_dir}" = /* ]]; then
  resolved_output_dir="${output_dir}"
else
  resolved_output_dir="${repo_root}/${output_dir}"
fi

archive_path="${resolved_output_dir}/${archive_name}"

echo "Current version: ${current_version}"
echo "Next version:    ${next_version}"
echo "Tag:             ${tag_name}"
echo "Archive:         ${archive_path}"

if [[ "${dry_run}" -eq 1 ]]; then
  echo "Dry run: would update ${assembly_info}"
  echo "Dry run: would package release with ${rsync_script}"
  echo "Dry run: would commit version bump and create git tag ${tag_name}"
  exit 0
fi

assembly_version="${next_version}.0"
mkdir -p "${resolved_output_dir}"
sed -i -E \
  -e "s/^(\[assembly: AssemblyVersion\(\")[0-9]+\.[0-9]+\.[0-9]+\.0(\"\\)\])$/\1${assembly_version}\2/" \
  -e "s/^(\[assembly: AssemblyFileVersion\(\")[0-9]+\.[0-9]+\.[0-9]+\.0(\"\\)\])$/\1${assembly_version}\2/" \
  "${assembly_info}"

if ! git -C "${repo_root}" diff --quiet -- "${assembly_info}"; then
  git -C "${repo_root}" add "${assembly_info}"
  git -C "${repo_root}" commit -m "Release ${next_version}"
else
  echo "Version file did not change after update attempt" >&2
  exit 1
fi

git -C "${repo_root}" tag "${tag_name}"

"${rsync_script}" "${temp_root}" "${mod_name}"
(cd "${temp_root}/Mods" && zip -rq "${archive_path}" "${mod_name}")

echo "Created release archive ${archive_path}"
echo "Created commit $(git -C "${repo_root}" rev-parse --short HEAD)"
echo "Created tag ${tag_name}"
