# 01 — SSH connect retry on session open

Status: done

## What to build

When opening an SSH session fails with a **connect failure**, automatically try again for that host before failing the host operation. This is **SSH connect retry**: fixed three attempts total, one-second wait after the first failure, two-second wait after the second, always-on retry lines that name the host and attempt, and fail-fast on **auth failure**. No new config keys or CLI flags.

Operators running any remote command (deploy image pull, app boot, details, etc.) recover from brief transport glitches—timeouts, connection refused/reset, unreachable—without restarting the whole run. Final failure after exhausted attempts remains a **connect failure** (exit 10 and connect marker). **Deploy connect retry** (`kamal deploy --retry`) stays a separate outer opt-in and is not changed.

Policy and constraints (from the spec):

- Layer: session open only—the path that today wraps transport failures as “SSH connection failed for …”. Do not re-run deploy phases or re-execute remote commands that already had a session.
- Filter: retry only when classification is connect (same rules as public failure class / deploy connect retry). Never retry auth.
- Jump host plus target open is one unit under the same loop.
- Each host retries independently; multi-host coordination is unchanged.
- Backoff must honour cancellation—if cancelled during the wait, do not start another attempt.
- Prefer a small, testable delay hook (same idea as deploy connect retry’s replaceable delay) rather than real wall-clock sleeps in tests.
- Keep existing pooled “no longer connected → rebuild” behaviour; this work targets failures while *establishing* a session, not idle eviction.

## Footprint

Projects: `src/Kamal`, `tests/Kamal.Tests`

- `src/Kamal/Execution/SshBackend.cs` — `ConnectAsync`, `ConnectViaJump`, `ConnectClientAsync`, `WrapConnectFailure` (session-open boundary where connect/auth wrapping happens today)
- `src/Kamal/Execution/SshConnectionPool.cs` — `GetAsync` (pool rebuild for stale sessions; do not conflate with SSH connect retry; only touch if the open factory wiring requires it)
- `src/Kamal/Cli/FailureClass.cs` — `FailureClasses.Classify`, connect vs auth types (reuse as the retry filter; do not change public exit/marker contract)
- `src/Kamal/Cli/ConnectRetry.cs` — prior art for replaceable `DelayAsync`, attempt loop, and connect-class filter (**deploy** connect retry; leave behaviour as outer layer)
- `tests/Kamal.Tests/Cli/ConnectRetryTests.cs` — prior art for attempt counts, backoff injection, class filter, always-on messaging
- `tests/Kamal.Tests/Cli/FailureClassTests.cs` — wrap/classify proofs for connect vs auth; final exit/marker expectations stay valid
- `tests/Kamal.Tests/Execution/` — new or extended tests at the session-open seam (injected connect/auth failures, delay hook, cancellation if practical); e.g. `SshConnectRetryTests.cs` if extracted

## Acceptance criteria

- [ ] Connect-class failure on first open, success on a later attempt → overall success, more than one open attempt, retry output present (host and attempt)
- [ ] Connect-class failure on every attempt → fails after three attempts; final class remains connect (and CI exit/marker semantics unchanged)
- [ ] Auth-class failure on first open → single attempt; class is auth; no connect-style retry loop
- [ ] Backoff between the three attempts is one second then two seconds when all fail (asserted via replaceable delay, not wall-clock)
- [ ] Cancellation during backoff prevents a further open attempt (when the seam makes this practical)
- [ ] Jump-plus-target open is treated as one retry unit; mid-command / non-open failures are not retried by this feature
- [ ] No new YAML/CLI knobs; **deploy connect retry** still works as the outer opt-in after SSH connect retry is exhausted
- [ ] `src/Kamal` and `tests/Kamal.Tests` are green

## Outcome

### Done
- Added `SshConnectRetry` (`src/Kamal/Execution/SshConnectRetry.cs`): fixed 3 attempts, backoff 1s then 2s via replaceable `DelayAsync`, connect-class filter via `FailureClasses.Classify`, always-on host/attempt retry lines.
- Wired session open through retry: `SshBackend.ConnectAsync` → `SshConnectRetry.RunAsync` → `ConnectOnceAsync` (semaphore held per attempt only; jump-plus-target remains one open unit).
- Tests: `tests/Kamal.Tests/Execution/SshConnectRetryTests.cs` — transient connect success, three-attempt connect exhaustion, auth fail-once, backoff delays, cancellation during backoff, single open unit per attempt, non-connect fail-once.
- Full suite green: 878 passed.

### Files
- `src/Kamal/Execution/SshConnectRetry.cs` (new)
- `src/Kamal/Execution/SshBackend.cs` (ConnectAsync / ConnectOnceAsync split)
- `tests/Kamal.Tests/Execution/SshConnectRetryTests.cs` (new)

### Deviations
- none (pool idle rebuild and deploy connect retry left unchanged; no YAML/CLI knobs)
