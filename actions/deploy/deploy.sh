#!/usr/bin/env bash
# Run `kamal deploy` with optional destination, connect-only --retry, and extra args.
set -euo pipefail

if ! command -v kamal >/dev/null 2>&1; then
  echo "kamal not found on PATH; run the setup action first." >&2
  exit 1
fi

args=(deploy)

if [[ -n "${DESTINATION:-}" ]]; then
  args+=(-d "${DESTINATION}")
fi

if [[ -n "${RETRY:-}" ]]; then
  args+=(--retry "${RETRY}")
fi

if [[ -n "${EXTRA_ARGS:-}" ]]; then
  # Allow quoted multi-token passthrough (e.g. '--skip-push --verbose').
  # shellcheck disable=SC2206
  eval "extra=(${EXTRA_ARGS})"
  args+=("${extra[@]}")
fi

echo "Running: kamal ${args[*]}"
exec kamal "${args[@]}"
