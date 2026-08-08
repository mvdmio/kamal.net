# 05 — Opt-in connect-only deploy retry

Status: done

## What to build

CI on flaky networks can retry deploys without log-scraping loops and without hiding permanent failures.

- Retry is **off by default**
- Opt-in via a flag such as `--retry N` on deploy (and setup if it composes deploy the same way); when enabled without a custom N, default **N = 3** attempts
- Retries **only connect** failures (failure class from step 04), with backoff
- Never retries auth, build, healthcheck, or lock failures (single attempt, original exit code and markers)

Operators see the same failure-class exit code after retries are exhausted. Interactive and careful runs stay non-retrying unless the flag is set.

## Footprint

Projects: `src/Kamal`, `tests/Kamal.Tests`

- `src/Kamal/Cli/KamalCli.cs` — global or deploy `--retry` option wiring
- `src/Kamal/Cli/MainCli.cs` — `Deploy` / `Setup` retry loop boundaries
- Failure-class mapping from step 04 — decide retryability by class
- `tests/Kamal.Tests/Cli/` — retry enabled: connect retried up to N; auth/build/healthcheck/lock fail once; disabled: no extra attempts (clock/backoff testable via injection if needed)

## Acceptance criteria

- [x] Without the flag, connect failures are not retried
- [x] With retry opted in, connect failures are attempted up to N (default 3) with backoff
- [x] Auth, build, healthcheck, and lock failures are never retried by this mechanism
- [x] Exhausted connect retries preserve connect exit code and markers
- [x] Tests cover enabled/disabled and non-retryable classes; `dotnet test` green

## Outcome

Added opt-in connect-only retry for `deploy` and `setup` via `--retry [N]` (omitted = off / 1 attempt; flag alone = 3; `--retry N` = max(1,N)). `ConnectRetry.RunAsync` wraps `MainCli.Deploy` / `Setup` boundaries; nested setup→deploy passes `connectRetryAttempts: 1` to avoid double-retry. Retries only when `FailureClasses.Classify` is connect; auth/build/healthcheck/lock/generic rethrow on first failure. Exponential backoff 1s/2s/4s… capped at 30s with injectable `DelayAsync` for tests; resets `Commander.Connected` between attempts. Exhausted connect failures rethrow original exception so exit 10 and `kamal.failure_class=connect` still come from `KamalCli.ReportFailure`. Tests: `ConnectRetryTests` (+ parse tree lines for `--retry`). `dotnet test`: 830 passed. Footprint matched; CI docs for the flag deferred to step 06.
