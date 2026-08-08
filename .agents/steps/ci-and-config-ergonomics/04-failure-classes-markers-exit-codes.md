# 04 — Failure classes, phase markers, and exit codes

Status: done

## What to build

CI authors and operators can classify a failed run without grepping free-form stack traces. Every run exposes a **failure class** (why it stopped) as both a stable process exit code and a greppable log marker, and **deploy phase** markers (where it was) that are distinct even when names overlap (CONTEXT.md vocabulary).

**Public failure classes and initial exit codes (contract once shipped):**

| Failure class | Exit code | Notes |
| --- | --- | --- |
| connect | 10 | Timeouts, dropped sockets, unreachable hosts |
| auth | 11 | Identity rejection / missing usable keys |
| build | 20 | Image build/push/clone failures |
| healthcheck | 30 | Container not healthy / barrier halt / boot endpoint failures of that class |
| lock | 40 | Deploy lock held / lock errors |
| generic | 1 | Everything else |

Do not reuse these codes for different classes later. Log markers for failure class and for deploy phase must be greppable and stable (exact marker strings chosen and used consistently in code + tests; CI docs will document them in a later step — introduce the strings here so they do not drift).

Wire mapping at the highest practical seam (`KamalCli.Start` / CLI exception handling and deploy orchestration): `BuildError` → build, `LockError` → lock, `HealthcheckError` / relevant `BootError` → healthcheck, SSH transport vs auth failures → connect / auth, else generic. Emit phase markers at least for major deploy stages (for example connect, build, boot) so “where” and “why” stay separate.

## Footprint

Projects: `src/Kamal`, `tests/Kamal.Tests`

- `src/Kamal/Cli/KamalCli.cs` — `Start` exit-code mapping (today always `1`)
- `src/Kamal/Cli/Errors.cs` — existing `BuildError`, `LockError`, `BootError`, …
- `src/Kamal/Cli/Healthcheck.cs` — `HealthcheckError`
- `src/Kamal/Cli/MainCli.cs` / `AppCli.cs` / deploy path — phase markers
- `src/Kamal/Execution/ExecuteError.cs` — SSH / command failures to classify where possible
- `src/Kamal/Execution/SshBackend.cs` — connect vs auth classification seams
- `tests/Kamal.Tests/Cli/` — CLI harness asserting exit codes and markers

## Acceptance criteria

- [x] Representative connect, auth, build, healthcheck, lock, and generic failures exit with the public codes above
- [x] Each class emits a greppable failure-class log marker
- [x] Deploy phases emit greppable phase markers separate from failure-class markers
- [x] Wrong SSH key classifies as auth (fail-fast, not connect) where the stack can tell them apart
- [x] Transport timeouts / dropped sockets classify as connect
- [x] Tests drive failures through CLI or commander-level seams; `dotnet test` green

## Outcome

Wired public failure classes at `KamalCli.Start` via `FailureClasses.Classify` / `ReportFailure`: connect=10, auth=11, build=20, healthcheck=30, lock=40, generic=1. Greppable markers `kamal.failure_class=<name>` and phase markers `kamal.phase=<name>` (connect/build/boot). Classification maps `BuildError`, `LockError`, `HealthcheckError`/`BootError`, SSH.NET auth vs connect types (plus message heuristics on `ExecuteError`). `SshBackend.WrapConnectFailure` preserves auth vs connect at the connect seam. Phase emit in `PreConnectIfRequired`, `MainCli` deploy/redeploy build, and `AppCli.Boot`. Tests: `FailureClassTests` plus updated lock/boot exit expectations. `dotnet test`: 808 passed. Footprint matched; docs deferred to step 06.
