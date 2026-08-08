# 08 — Passphrase-capable keys and opt-in host-key policy

Status: done

## What to build

Close remaining SSH hard edges without breaking existing deploys.

**Passphrase-protected keys**

- Configured or default private keys must not be silently skipped when they are unreadable only because of a passphrase
- Loading succeeds when a passphrase is available (env/config mechanism chosen by implementer and documented; interactive prompt only when a TTY is present)
- Non-interactive / non-TTY runs fail clearly when only an encrypted key is available without a passphrase — no hang on a prompt
- CI continues to use agent, `KAMAL_SSH_PRIVATE_KEY`, `key_data`, or unencrypted keys as the happy path

**Host-key policy**

- Keep today’s permissive host-key behaviour as the **default** (existing deploys do not break)
- Add an opt-in strict / known_hosts policy knob for teams that need verification

Update embedded SSH docs for the new knob and passphrase expectations. Password-based SSH remains out of scope.

## Footprint

Projects: `src/Kamal`, `tests/Kamal.Tests`

- `src/Kamal/Execution/` — credential loading (passphrase), host-key verification policy on clients
- `src/Kamal/Execution/SshBackend.cs` / `SshPortForwarding.cs` — apply policy and passphrase-capable load
- `src/Kamal/Configuration/Ssh.cs` — opt-in host-key / known_hosts surface if config-driven
- `src/Kamal/Configuration/Docs/ssh.yml` — document knob and passphrase behaviour
- `tests/Kamal.Tests/Execution/`, `tests/Kamal.Tests/Configuration/SshTests.cs` — passphrase not silently skipped; non-TTY fails clearly; strict mode opt-in; default remains permissive

## Acceptance criteria

- [x] Passphrase-protected configured keys are not silently skipped
- [x] With a passphrase available, encrypted keys load successfully
- [x] Without a TTY and without a passphrase, runs fail clearly (no indefinite prompt)
- [x] Host-key verification remains permissive by default
- [x] Opt-in strict / known_hosts mode enables verification for teams that set the knob
- [x] Docs cover the new behaviour; tests assert external behaviour; `dotnet test` green

## Outcome

Passphrase-capable key loading in `SshCredentials`: encrypted keys from `keys`/`key_data`, `KAMAL_SSH_PRIVATE_KEY`, and default `id_*` are never silently skipped for passphrase alone. Passphrase sources: `KAMAL_SSH_PASSPHRASE`, `ssh.passphrase` (secret name or value), then TTY prompt only when interactive. Non-TTY without passphrase fails with a clear `InvalidOperationException` (no hang). Host-key policy remains permissive by default; opt-in `ssh.strict_host_key_checking` verifies against `known_hosts` (default `~/.ssh/known_hosts` or `ssh.known_hosts`) via `SshHostKeyPolicy` on `SshBackend` / SFTP / port-forwarding clients (logical host through jumps). Docs updated in `ssh.yml` and a CI note. Tests: passphrase load/fail/prompt and known_hosts trust in `SshCredentialsTests` / `SshHostKeyPolicyTests` / `SshTests`. `dotnet test`: 861 passed. Footprint matched.
