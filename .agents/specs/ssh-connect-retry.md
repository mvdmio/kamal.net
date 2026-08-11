# SSH connect retry

Status: ready-for-agent

## Problem Statement

Operators run `kamal deploy` (and other remote commands) against hosts that are up and correctly configured. Sometimes the host does not accept an SSH connection on the first try — the session open times out or is refused — and a second try a moment later succeeds.

Today Kamal.NET fails that host operation as a **connect failure** and stops the run. The only product recovery is **deploy connect retry** (`kamal deploy --retry`), which re-runs the entire deploy body. That is too heavy: the image may already be built and pushed, and the operator only needed another attempt to open SSH.

## Solution

Add **SSH connect retry**: when opening an SSH session fails with a **connect failure**, automatically try again for that host before failing the operation. Use fixed defaults (three attempts total, short backoff). Always tell the operator when a retry happens.

Never retry an **auth failure**. Leave **deploy connect retry** as a separate opt-in for when a connect failure still ends the run after SSH connect retry.

## User Stories

1. As an operator, I want a flaky SSH accept during image pull to succeed on a later attempt in the same run, so that I do not restart deploy after a one-off timeout.
2. As an operator, I want the same behaviour during app boot and other remote steps, so that any command that opens SSH benefits, not only deploy.
3. As an operator, I want connection refused, reset, and unreachable errors retried the same way as timeouts, so that brief transport glitches share one recovery path.
4. As an operator, I want bad SSH keys to fail on the first attempt as an **auth failure**, so that retries never mask a credentials problem.
5. As an operator, I want at most three attempts to open a session, so that a host that is truly down fails after a bounded number of tries rather than retrying forever.
6. As an operator, I want a short wait between attempts (one second after the first failure, two seconds after the second), so that a restarting sshd can recover without a tight spin.
7. As an operator, I want a clear line printed on each retry (host and attempt), so that a multi-second pause does not look like a hang.
8. As an operator, I want the final failure after exhausted attempts to remain a **connect failure** (exit 10 and the connect marker), so that CI scripts keep branching the same way.
9. As an operator, I want no new config keys or flags for this, so that the fix works without editing `deploy.yml`.
10. As an operator, I want optional `kamal deploy --retry` to still work after SSH connect retry is exhausted, so that I can re-run the full deploy when I choose.
11. As an operator, I want cancelling the run during a backoff wait to stop further attempts, so that Ctrl-C does not open another connection.
12. As an operator of many hosts, I want each host to retry independently, so that one flaky host does not change how others connect.
13. As an operator using a jump host, I want the whole session open (jump plus target) retried as one unit, so that a glitch on either side can recover without partial state.
14. As an operator, I want mid-command failures (non-zero remote exit, healthcheck) not retried by this feature, so that only session open is automatic.
15. As a CI consumer, I want exit codes and failure-class markers unchanged on final failure, so that pipelines that already key off connect vs auth keep working.
16. As a contributor, I want tests that prove connect-class opens are retried and auth-class opens are not, so that the policy does not regress.

## Implementation Decisions

1. **Layer**: Implement SSH connect retry on SSH **session open** only (the path that today wraps transport failures as “SSH connection failed for …”). Do not re-run deploy phases or re-execute remote commands that already had a session.
2. **Filter**: Retry only when the failure classifies as **connect failure** (timeouts, connection refused/reset, unreachable, dropped socket, and the existing connect-shaped wrappers). Do not retry **auth failure** (authentication exceptions, missing passphrase, auth-shaped wrappers).
3. **Policy (fixed)**: Three attempts total. After attempt 1 fails, wait one second; after attempt 2 fails, wait two seconds; then surface the last connect error. No new `sshkit` or CLI settings.
4. **Visibility**: On each retry (before the next attempt), print one always-on line that names the host and the attempt (same spirit as deploy connect retry messaging). Do not gate this on verbose mode.
5. **Cancellation**: Backoff must honour cancellation; if cancelled during the wait, do not start another attempt.
6. **Parallelism**: Leave multi-host coordination as it is; each host’s session open retries on its own.
7. **Jump hosts**: Treat jump plus target open as a single unit under the same retry loop.
8. **Relationship to deploy connect retry**: Unchanged and outer. SSH connect retry runs first inside the connection path. Deploy connect retry still re-enters the full deploy body only when the operator passes `--retry` and the run still ends as connect.
9. **Reuse**: Use the same connect-vs-auth criteria as public failure classification (typed transport vs authentication failures and their wrappers — the same rules deploy connect retry uses). Prefer a small, testable delay hook for backoff (same idea as deploy connect retry’s replaceable delay) rather than real wall-clock sleeps in tests.
10. **Pool / stale sessions**: Keep existing “pooled connection no longer connected → rebuild” behaviour. This work targets failures thrown while *establishing* a session, not idle eviction.
11. **Glossary**: Domain terms live in the project glossary: **SSH connect retry**, **deploy connect retry**, **connect failure**, **auth failure**.

## Testing Decisions

1. **Good tests** assert external behaviour: how many open attempts occur, which exception classes are retried, final failure class/exit semantics, that retry lines appear, and that auth fails once. They do not assert private method structure or exact string formatting beyond what operators and CI rely on.
2. **Seam**: Inject connect/auth failures at the session-open boundary (or a factory used only for open), and replace the delay function so tests do not wait on real timers — prior art is deploy connect retry tests.
3. **Cover at least**:
   - Connect-class failure on first open, success on a later attempt → overall success, more than one open attempt, retry output present.
   - Connect-class failure on every attempt → fails after three attempts, final class is connect.
   - Auth-class failure on first open → single attempt, class is auth, no connect-style retry loop.
   - Backoff delays are one second then two seconds between the three attempts when all fail.
   - Cancellation during backoff prevents a further open attempt (if the seam makes this practical).
4. **Prior art**: Deploy connect retry tests (attempt counts, backoff, class filter); failure-class tests that already prove wrap/classify for connect vs auth; execution tests around the SSH backend and pool where connect failures are constructed.
5. **Avoid** live SSH to a real host for this policy; use fakes or injected failures at the open seam.

## Out of Scope

- Changing the default SSH connection timeout (currently a fixed library timeout).
- Wiring or changing unused DNS retry config.
- Making SSH connect retry configurable in YAML or CLI.
- Retrying failed remote *commands* after a session is open (docker exit codes, healthcheck, hooks).
- Turning on deploy connect retry by default.
- Improving error copy alone without automatic retry.
- Mid-pool idle eviction policy changes.
- Password authentication or new credential sources.

## Further Notes

- Operator trigger that drove this work: deploy failed in image pull with `SshOperationTimeoutException` / “SSH connection failed … Connection has timed out” against a host that was reachable; an immediate manual retry worked. Full redeploy was the only recovery.
- Two layers remain intentional: **SSH connect retry** absorbs transient accept glitches inside one operation; **deploy connect retry** is the heavy opt-in when the operator wants a full redeploy after connect still fails.
- Auth must stay fail-fast so retries never look like “eventual success” for wrong keys.
