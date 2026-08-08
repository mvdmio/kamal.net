# CI and config ergonomics for suite-style consumers

Status: ready-for-agent

## Problem Statement

Teams that adopt Kamal.NET for multi-destination deploys on GitHub Actions cannot treat the tool as a drop-in for “install and run `kamal deploy`.” They must write private keys to disk by hand because ssh-agent is unsupported, invent a replacement for ERB-driven build args and env values, scrape logs to retry flaky SSH, and re-derive tool install, PATH, and version pins in every workflow. Operators also hit silent quote and flag damage on `accessory exec` (for example guest `-c`), and passphrase-protected or strict host-key needs are unhandled or surprising. The result is a large custom composite action and secrets workarounds in every serious consumer.

## Solution

Kamal.NET gains first-class CI and config ergonomics so a suite-shaped consumer can drop custom SSH key scaffolding, ERB workarounds, install glue, and log-scraping retry loops.

Operators authenticate over SSH via ssh-agent, a single private-key environment variable, or existing `key_data` secrets (documented). Config supports limited env expansion in all string values after YAML load. Runs expose stable failure classes through exit codes and log markers; optional connect-only retries cover flaky networks. This repo publishes `setup` and `deploy` GitHub Actions and CI docs. `accessory exec` honors end-of-options and passes remote argv intact. Passphrase-capable keys and an opt-in strict host-key mode close the remaining SSH hard edges. Full ERB and other non-blocking Ruby fidelity items stay out of scope.

## User Stories

1. As a CI author, I want Kamal to use keys loaded in ssh-agent, so that I can keep the standard GitHub Actions ssh-agent pattern.
2. As a CI author, I want to set one environment variable with a private key, so that I can authenticate without writing key files or changing `deploy.yml`.
3. As a CI author, I want documented `ssh.key_data` + secrets usage, so that I can inject keys without agent or disk files when that fits our secret model.
4. As a CI author, I want a clear order of SSH credentials when several are present, so that auth behavior is predictable across local and CI.
5. As an operator, I want passphrase-protected private keys to work when a passphrase is available, so that encrypted keys are not silently skipped.
6. As a CI author, I want non-interactive runs to fail clearly when only an encrypted key is available without a passphrase, so that CI does not hang on a prompt.
7. As a security-conscious operator, I want an opt-in strict host-key / known_hosts mode, so that teams that need verification can enable it without changing the default for everyone else.
8. As an operator, I want today’s permissive host-key behavior to remain the default, so that existing deploys do not break.
9. As a config author, I want `${ENV_VAR}` and `${ENV_VAR:-default}` in any string value in `deploy.yml`, so that I can replace common ERB `ENV.fetch` patterns without full ERB.
10. As a config author, I want bare `${VAR}` to fail at config load when `VAR` is unset, so that misconfigured CI fails before connect.
11. As a config author, I want `${VAR:-}` or `${VAR:-default}` for optional values, so that empty or defaulted values stay explicit.
12. As a config author, I want expansion to use the process environment only (not the secrets map as expansion source), so that secret name-references stay the path for sensitive material.
13. As a CI author, I want a GitHub Action that installs a pin-able Kamal.NET version and puts it on PATH, so that every workflow does not re-derive `dotnet tool install`.
14. As a CI author, I want that setup Action to configure SSH from inputs, so that deploy and non-deploy `kamal` commands share one setup step.
15. As a CI author, I want a deploy Action that runs setup then `kamal deploy` with destination and extra args, so that the happy path is one step.
16. As a CI author, I want Actions to live in this repository and track tool releases, so that Action and tool versions stay coupled.
17. As a CI author, I want optional connect-only deploy retries behind an explicit flag, so that flaky SSH can be retried without hiding build or healthcheck failures.
18. As a CI author, I want retry off by default, so that interactive and careful runs are not surprised by automatic re-runs.
19. As a CI author, I want a sensible default retry count when I opt in (three attempts), so that I do not have to invent N for the common case.
20. As a CI author, I want auth, build, healthcheck, and lock failures never retried by that mechanism, so that bad credentials and bad images fail fast.
21. As a CI author, I want distinct process exit codes per failure class, so that scripts can branch without grepping logs.
22. As a CI author, I want greppable log markers for failure class and deploy phase, so that annotations and humans can classify runs the same way.
23. As an operator running `accessory exec`, I want flags after `--` to go to the remote command, so that guest tools can use short options like `-c`.
24. As an operator running `accessory exec`, I want remote argv passed without re-shelling or re-splitting, so that quoting matches what I typed.
25. As an operator, I want the global short `-c` for config file to remain, so that existing scripts keep working while end-of-options fixes collisions.
26. As a new adopter, I want a single CI documentation entry (`kamal docs ci` or equivalent section), so that install, SSH, expansion, destinations, failure classes, and a GHA example live in one place.
27. As a suite maintainer, I want to delete custom key-write steps, commit-SHA secrets workarounds driven only by missing expansion, install/PATH glue, and log-scrape retry loops after this lands, so that deploy workflows shrink to setup/deploy Action usage.
28. As a local developer, I want the same SSH and expansion rules as CI, so that local `kamal deploy` and CI do not diverge on auth or config.
29. As a multi-destination suite owner, I want destination support in the deploy Action and docs, so that auth, web, and other destinations share one pattern.
30. As an operator diagnosing a failed run, I want phase markers separate from failure-class markers, so that “where it was” and “why it stopped” stay distinct.
31. As a consumer on a flaky network, I want connect timeouts and dropped sockets classified as connect failures, so that opt-in retry applies to the right cases.
32. As a consumer with a wrong SSH key, I want an auth failure class and fail-fast behavior, so that retry does not burn minutes on a permanent mistake.
33. As a release engineer, I want these improvements versioned as port (patch) work unless we intentionally track a new upstream minor, so that versioning stays honest to Kamal.NET rules.
34. As an operator reading docs, I want “migrating from ERB” guidance that points at config expansion and secrets patterns, so that Ruby Kamal migrants know the supported path.
35. As a CI author using only `key_data` secrets, I want that path to remain first-class in docs even after agent and env support land, so that secret-manager-centric setups stay valid.

## Implementation Decisions

1. **Success bar** — Design and acceptance center on a suite-shaped consumer (destinations, secrets, GitHub Actions) being able to drop custom SSH key writes, ERB/build-arg workarounds, install/PATH glue, and log-scraping retry — not on abstract “any CI” alone, and not on docs-only guidance.

2. **Consumer-first product stance** — Prefer the smallest surfaces that fix .NET CI even without a Ruby twin. Prefer matching Ruby when Ruby already solves the pain (ssh-agent). Recorded in ADR 0001.

3. **SSH credentials — supported sources**
   - ssh-agent identities
   - `KAMAL_SSH_PRIVATE_KEY` environment variable (PEM material)
   - `ssh.keys` paths and `ssh.key_data` (secret names or deprecated inline PEM)
   - Default `~/.ssh/id_*` files when nothing else yields keys

4. **SSH credential priority** — Explicit `ssh.keys` / `ssh.key_data` first; then `KAMAL_SSH_PRIVATE_KEY` if set; then ssh-agent; then default identity files.

5. **Passphrase-protected keys** — Configured or default keys must not be silently skipped when unreadable due to passphrase. Support loading when a passphrase is available. Interactive passphrase prompt only when a TTY is present. CI must use agent, env key, `key_data`, or an unencrypted key.

6. **Host-key policy** — Keep current permissive default. Add an opt-in strict / known_hosts policy knob for teams that need verification.

7. **Config expansion** — After YAML load, expand `${ENV_VAR}` and `${ENV_VAR:-default}` in every string scalar in the config tree from the process environment. Recorded in ADR 0002. Not full ERB. Do not treat secrets map values as the expansion source for these placeholders.

8. **Missing env vars** — Bare `${VAR}` with `VAR` unset is a config load error. Optional values must use `${VAR:-default}` or `${VAR:-}`.

9. **GitHub Actions location** — Publish under this repository: `actions/setup` and `actions/deploy` (consumers reference this repo’s action paths at a version tag).

10. **Setup Action** — Installs a pin-able `mvdmio.Kamal` tool version, ensures PATH, configures SSH from inputs (private key and/or agent-oriented inputs as appropriate). Usable alone for non-deploy `kamal` commands.

11. **Deploy Action** — Composes setup, then runs `kamal deploy` with destination and passthrough arguments.

12. **Deploy retry** — Off by default. Opt-in via a flag such as `--retry N` (default N = 3 when enabled without a stronger product reason to pick another number). Retries only **connect** failures with backoff. Never retries auth, build, healthcheck, or lock failures.

13. **Failure classes (public contract)** — At least: connect, auth, build, healthcheck, lock, plus a generic failure for everything else. Each class maps to a stable process exit code and a greppable log marker.

14. **Deploy phases** — Log stable phase markers (for example connect, build, boot) separately from failure-class markers so “where” and “why” stay distinct.

15. **Exit code mapping (initial public set)** — Reserve distinct non-zero codes for the named classes (exact numbers are part of the public contract once shipped; pick a sparse set such as connect=10, auth=11, build=20, healthcheck=30, lock=40, generic=1 or another unused code). Document them in CI docs. Do not reuse codes for different classes later.

16. **accessory exec** — Stop option parsing at end-of-options (`--`). Pass remote argv through without re-shelling or re-splitting. Keep global short `-c` as config-file.

17. **CI documentation** — Add `kamal docs ci` or an equivalent dedicated CI section covering: tool install and version pin, SSH (agent, env var, `key_data`), config expansion and ERB migration, destinations, failure classes and exit codes, retry flag, and a GitHub Actions example.

18. **Ship order** — SSH auth → config expansion → failure classes / markers / exit codes → opt-in retry → Actions + CI docs → accessory exec → passphrase support + host-key knob. Order is implementation sequencing, not a reduction of scope; all items above are in scope.

19. **Versioning** — These are port improvements. Bump the patch component of the tool version per project versioning rules unless the work is deliberately tied to a new upstream minor.

20. **Domain language** — Use terms in root `CONTEXT.md`: Destination, Secret, Failure class, Connect failure, Auth failure, Config expansion, Deploy phase.

## Testing Decisions

1. **Good tests** assert external behavior: CLI exit codes, log markers operators can grep, config load success/failure messages, SSH auth method selection given env and config, Action inputs’ observable effects where testable, and remote argv shape for `accessory exec`. Prefer not to lock tests to private method names or internal exception type strings unless those types are the documented public surface.

2. **Config expansion** — Cover: expansion in nested string scalars; `${VAR:-default}` and `${VAR:-}`; bare `${VAR}` unset → load error; set var → substituted value; no expansion of non-strings; interaction with destination overlay merge (expand after merge of base + destination). Prior art: configuration and dotenv substitution tests.

3. **SSH auth** — Cover: priority order among keys, `key_data`, env private key, and agent (agent may use fakes or integration seams already used by the SSH backend tests); passphrase path does not silently skip configured keys; non-TTY does not block forever without credentials. Prior art: SSH configuration tests and execution-layer tests with fakes.

4. **Failure classes and exit codes** — Drive failures through the highest practical seam (CLI entry or commander-level run) and assert process exit code and presence of class/phase markers. Map representative cases: connect, auth, build, healthcheck, lock, generic. Prior art: CLI harness tests that invoke parse/execute paths.

5. **Retry** — With retry enabled, connect-class failures are attempted up to N with backoff boundaries testable via injected clock or stubbed connect; auth/build/healthcheck/lock fail once. With retry disabled, no extra attempts.

6. **accessory exec** — Parse/invoke tests: after `--`, `-c` and similar tokens remain in the remote command; multi-word remote commands keep intended argv boundaries (no re-split damage). Prior art: CLI parse-tree and accessory CLI tests.

7. **GitHub Actions** — Prefer lightweight validation of Action metadata and any install scriptlets (action.yml inputs/outputs, shellcheck-level sanity if scripts exist). Full end-to-end deploy on real hosts is out of band for the unit suite.

8. **Docs** — Smoke that `kamal docs ci` (or chosen section name) returns the CI content and that exit-code table matches the public contract.

## Out of Scope

- Full ERB (or other embedded scripting) in `deploy.yml`
- Password-based SSH authentication
- Chained jump hosts and `proxy_command` support beyond what already exists or is explicitly rejected today
- OpenTelemetry or external audit log shipping
- Faithfulness to Ruby gem install, bundler, or `kamal init --bundle` flows
- Forcing all dynamic values through secrets forever (config expansion is the chosen alternative)
- Renaming or removing global short `-c` for config file
- Making host-key verification strict by default
- Default-on deploy retry for all runs
- Retrying auth, build, healthcheck, or lock failures
- A separate repository for the GitHub Actions
- Builder-args-from-secrets as a separate feature (config expansion covers env-driven args; secrets name-references for sensitive build secrets remain as today via `builder.secrets`)

## Further Notes

- Source idea: `.agents/ideas/ci-and-config-ergonomics.md` (raised from mvdmio-suite migration friction).
- ADRs: `docs/adr/0001-consumer-first-ci-ergonomics.md`, `docs/adr/0002-config-expansion-not-full-erb.md`.
- Glossary: root `CONTEXT.md`.
- Acceptance reference (not a hard dependency on that repo’s tree): after this work, a suite-style consumer should be able to replace custom kamal-deploy composites with the official Actions and drop log-scrape retry and key-file write steps, using config expansion for commit SHA–style build metadata instead of ERB or inventing a second channel.
- Exact exit code integers should be written into CI docs and tests in the same change that introduces them so the public contract does not drift between code and docs.
