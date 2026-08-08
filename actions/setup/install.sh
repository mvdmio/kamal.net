#!/usr/bin/env bash
# Install mvdmio.Kamal as a global dotnet tool and put ~/.dotnet/tools on PATH.
set -euo pipefail

tools_dir="${HOME}/.dotnet/tools"
mkdir -p "${tools_dir}"
echo "${tools_dir}" >> "${GITHUB_PATH}"
export PATH="${tools_dir}:${PATH}"

version_args=()
if [[ -n "${KAMAL_VERSION:-}" ]]; then
  version_args+=(--version "${KAMAL_VERSION}")
fi

# update fails when missing; install fails when present — try both.
if ! dotnet tool update -g mvdmio.Kamal "${version_args[@]}" --verbosity quiet 2>/dev/null; then
  dotnet tool install -g mvdmio.Kamal "${version_args[@]}" --verbosity quiet
fi

if ! command -v kamal >/dev/null 2>&1; then
  echo "kamal was not found on PATH after install (expected ${tools_dir})" >&2
  exit 1
fi

echo "Installed: $(kamal version)"
