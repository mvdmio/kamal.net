# 01 — Prefactor shared SSH credential loading

Status: done

## What to build

SSH private-key loading and connection-info construction today live in two parallel copies (`SshBackend` and `SshPortForwarding`). Before adding new credential sources, collapse that into one shared seam so later auth work has a single place to change and both call sites stay identical.

No user-visible behaviour changes: same keys, same fallbacks, same silent skip of unreadable default identity files, same auth methods presented to SSH.NET. Existing tests stay green without new product tests.

## Footprint

Projects: `src/Kamal`, `tests/Kamal.Tests`

- `src/Kamal/Execution/SshBackend.cs` — `LoadKeyFiles`, `BuildConnectionInfo` (callers of the shared helper)
- `src/Kamal/Execution/SshPortForwarding.cs` — duplicate `LoadKeyFiles`, `BuildConnectionInfo`
- `src/Kamal/Execution/` — new shared credential/connection helper type (name chosen by implementer)

## Acceptance criteria

- [x] `SshBackend` and `SshPortForwarding` load private keys and build connection info through one shared implementation
- [x] Default identity-file fallback and silent skip of unreadable default keys behave as before
- [x] `dotnet test` is green with no intentional product behaviour change

## Outcome

Landed pure prefactor: extracted duplicate `LoadKeyFiles` / `BuildConnectionInfo` / `ExpandHome` into `internal static class SshCredentials` at `src/Kamal/Execution/SshCredentials.cs`. Both `SshBackend` and `SshPortForwarding` now thin-wrap `SshCredentials.BuildConnectionInfo`. Behaviour unchanged (configured keys, `key_data`, default `~/.ssh/id_*` fallback, silent skip of unreadable default keys, private-key + none auth methods, 30s timeout). Footprint matched; helper named `SshCredentials` rather than a longer factory name. `dotnet test`: 768 passed.
