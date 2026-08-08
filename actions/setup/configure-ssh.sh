#!/usr/bin/env bash
# Export a PEM private key as KAMAL_SSH_PRIVATE_KEY for subsequent steps (and kamal).
# Prefer this over writing key files. Leave ssh-private-key empty to use ssh-agent or key_data.
set -euo pipefail

if [[ -z "${SSH_PRIVATE_KEY:-}" ]]; then
  echo "SSH_PRIVATE_KEY is empty; nothing to configure." >&2
  exit 1
fi

{
  echo "KAMAL_SSH_PRIVATE_KEY<<KAMAL_SSH_KEY_EOF"
  # Preserve the PEM exactly (including a trailing newline).
  printf '%s\n' "${SSH_PRIVATE_KEY}"
  echo "KAMAL_SSH_KEY_EOF"
} >> "${GITHUB_ENV}"

echo "Configured KAMAL_SSH_PRIVATE_KEY for Kamal SSH authentication."
