# 02 — SSH auth sources and credential priority

Status: done

## What to build

Operators and CI can authenticate over SSH without writing key files by hand. Kamal accepts these credential sources:

1. Explicit `ssh.keys` paths and `ssh.key_data` (secret names or deprecated inline PEM) — already present
2. `KAMAL_SSH_PRIVATE_KEY` environment variable (PEM material)
3. ssh-agent identities
4. Default `~/.ssh/id_*` files when nothing else yields keys

**Priority (public contract):** explicit `ssh.keys` / `ssh.key_data` first; then `KAMAL_SSH_PRIVATE_KEY` if set; then ssh-agent; then default identity files. When only higher-priority sources are configured, lower ones are not mixed in unless product rules say otherwise (`keys_only` continues to mean “do not pull extra agent identities when explicit keys are in use,” as documented in `ssh.yml`).

`key_data` remains first-class (secret name resolution already works). Local and CI share the same rules. Passphrase-protected keys and host-key policy knobs are **not** in this step (later).

External behaviour: given env and config, the backend selects auth methods in that order; wrong or missing usable credentials fail at connect time rather than silently falling through to password (password auth stays out of scope).

## Footprint

Projects: `src/Kamal`, `tests/Kamal.Tests`

- `src/Kamal/Execution/` — shared SSH credential helper from step 01; agent and env-key loading
- `src/Kamal/Execution/SshBackend.cs` — connection uses expanded credential set
- `src/Kamal/Execution/SshPortForwarding.cs` — same credential set for port forwards
- `src/Kamal/Configuration/Ssh.cs` — only if config surface needs a knob; prefer env + existing keys/key_data
- `src/Kamal/Configuration/Docs/ssh.yml` — document agent / `KAMAL_SSH_PRIVATE_KEY` / priority if the embedded docs are the right home
- `tests/Kamal.Tests/Configuration/SshTests.cs` — prior art for key_data
- `tests/Kamal.Tests/Execution/` — auth-method selection / priority tests (fakes or seams)

## Acceptance criteria

- [x] With only `KAMAL_SSH_PRIVATE_KEY` set and no `ssh.keys`/`key_data`, connections use that PEM material
- [x] ssh-agent identities are used when agent has keys and higher-priority sources do not supply keys
- [x] Explicit `ssh.keys` / `ssh.key_data` win over env key and agent
- [x] Priority order matches the public contract when several sources are present
- [x] Default `~/.ssh/id_*` still used only when nothing else yields keys
- [x] Tests assert external selection behaviour; `dotnet test` green

## Outcome

Extended `SshCredentials` (step 01) with exclusive-priority resolution: configured `keys`/`key_data` → `KAMAL_SSH_PRIVATE_KEY` → ssh-agent (`SshNet.Agent` package) → default `~/.ssh/id_*`. Higher sources that yield keys do not mix lower ones (covers `keys_only` intent for agent when explicit keys are used). Agent failures / missing `SSH_AUTH_SOCK` fall through; password auth still out of scope (`NoneAuthenticationMethod` only). Injectable `SshCredentialLoadOptions` seams for env/agent/defaults; `SshCredentialSource` exposes which source won. Documented sources and priority in `Configuration/Docs/ssh.yml`. Added `InternalsVisibleTo` for `Kamal.Tests`. No changes needed to `Ssh.cs` config surface. Tests: `tests/Kamal.Tests/Execution/SshCredentialsTests.cs` (12 cases). `dotnet test`: 780 passed. Footprint matched; package addition `SshNet.Agent` 2024.2.0.5.
