# Idea — CI and config ergonomics for real suite consumers

Status: idea  
Raised: 2026-08-08  
Source: adopting Kamal.NET in [mvdmio-suite](https://github.com/mvdmio/mvdmio-suite) deploy pipelines

## Motivation

Migrating mvdmio-suite from Ruby Kamal to Kamal.NET was mostly a drop-in for `config/deploy.yml` and the CLI surface — but two known deviations forced non-trivial workarounds in GitHub Actions and config:

1. **No ssh-agent** — CI could no longer use `webfactory/ssh-agent`; each workflow writes a private key into `~/.ssh/id_*` by hand.
2. **No ERB in `deploy.yml`** — a single `<%= ENV.fetch("DEPLOYED_COMMIT_SHA", "") %>` build-arg had to move into secrets + runtime `env.secret`.

The suite also carries ~40 lines of bash that greps deploy logs for transient SSH failures and retries. That is load-bearing on flaky CI networks and should not live in every consumer.

Closing these gaps would shrink every .NET shop's deploy action to roughly "install tool → `kamal deploy`".

## Goal

A consumer with destinations, secrets, and GitHub Actions can adopt Kamal.NET without:

- custom SSH key-file scaffolding,
- inventing an ERB replacement for env-driven config values,
- scraping logs to classify transient vs hard failures,
- re-deriving install/PATH/version-pin steps in every repo.

## Improvements (by impact for suite-style consumers)

### 1. SSH auth for CI — highest impact

**Pain today:** Kamal.NET ignores ssh-agent. One GHA step becomes a ~20-line script (key type detection, write `id_ed25519`/`id_rsa`/`id_ecdsa`, chmod), duplicated across deploy and proxy-reboot workflows.

**Ship one of:**

| Improvement | Why |
| --- | --- |
| **ssh-agent support** (SSH.NET can use an agent) | Restores the standard GHA pattern; zero custom scripting |
| **Documented CI path via `ssh.key_data` secrets** | Secret *names* already resolve in `key_data` (e.g. `ssh.key_data: [SSH_PRIVATE_KEY]` + secrets `SSH_PRIVATE_KEY=$(printenv SSH_PRIVATE_KEY)`). Avoids writing key files *if* docs lead with it |
| **`KAMAL_SSH_PRIVATE_KEY` env convenience** | One env var CI always has; no `deploy.yml` change |

Priority: **ssh-agent first**. File/`key_data` is a workable fallback; agent is what every GHA tutorial assumes.

### 2. Dynamic config without full ERB

**Pain today:** Dynamic build-args / env values that used `<%= ENV.fetch(...) %>` cannot stay in YAML. Consumers invent secrets-command or pre-process YAML.

**Ship one of (not full ERB):**

1. **`${ENV_VAR}` / `${ENV_VAR:-default}` expansion** in string scalars after YAML load  
2. **Builder args from secrets** — allow `builder.args` to pull secret names the way `env.secret` does (BuildKit `builder.secrets` already exists; args do not)  
3. **Document the secrets-command pattern as the ERB replacement** — a short “migrating from ERB” page with worked examples

Full ERB is the wrong port target. Limited expansion covers most real ERB use (`ENV.fetch`, empty defaults).

### 3. Official GitHub Action

**Pain today:** Every consumer reinvents setup-dotnet, tool install at a pinned version, PATH, SSH, and optional retry.

**Ship:**

```yaml
- uses: mvdmio/kamal.net/actions/deploy@v2
  with:
    destination: auth
    ssh-private-key: ${{ secrets.SSH_PRIVATE_KEY }}
    # optional: version from the repo's dotnet-tools.json
```

Even a thinner `setup` action (install tool + SSH only) lets consumer composites shrink to “run kamal + post-deploy hooks”.

### 4. Built-in retry for transient SSH

**Pain today:** Consumers grep logs for `SshConnectionException|SocketException|…` and retry `kamal deploy` in a bash loop. Build/healthcheck failures must still fail fast.

**Ship:**

```bash
kamal deploy -d auth --retry 3 --retry-on-transient
```

or always retry **connect-phase** failures N times with backoff, while failing fast on build/healthcheck/lock errors.

Classify failures: **connect / auth / build / healthcheck / lock** so CI can decide without scraping logs. Exit codes by class (e.g. 10 = SSH, 20 = build, 30 = healthcheck) would make retry trivial.

### 5. `accessory exec` argument hygiene

Already a documented landmine for operators (not only CI):

- `-c` is claimed as `--config-file` even after `--`
- Quoting for remote commands is mangled (`psql -c '…'` arrives split)

**Ship:**

- Stop parsing flags after `--` (true end-of-options)
- Pass the remote argv through without re-shelling / re-splitting
- Prefer `--config` / drop short `-c` for the config path if it collides with common guest tools

Silent quote mangling already caused a Bugsink bootstrap failure (role created without the intended password).

### 6. Smaller quality-of-life

| Item | Why it helps suite consumers |
| --- | --- |
| Versioned GHA action / clear install docs for `dotnet tool install -g mvdmio.Kamal --version X` | Consumers parse `dotnet-tools.json` to keep CI and local aligned |
| Stable, greppable log markers (`KAMAL_PHASE=connect\|build\|boot\|…`) | Retry classification and CI annotations |
| Exit codes by failure class | Retry only connect failures |
| `kamal docs ci` (or a README “CI” section) | Install, SSH, secrets, no-ERB, destinations, GHA example in one place |
| Optional known_hosts / host-key policy knob | SSH.NET is permissive today (fine for many); some teams need strict mode later |
| Passphrase-protected keys | Default `id_*` files currently fall through silently when unreadable |

## Out of scope / do not prioritize

These did not block suite adoption and should stay low priority unless a real consumer needs them:

- Full ERB in `deploy.yml`
- Password auth
- Chained jump hosts / `proxy_command`
- OpenTelemetry audit log shipping
- Faithfulness to Ruby gem install / bundler / `kamal init --bundle` flows

## If only three ship

1. **ssh-agent** (or first-class `KAMAL_SSH_PRIVATE_KEY` + docs for `key_data` secrets)  
2. **`${ENV}` expansion** (or builder-args-from-secrets) as the ERB replacement  
3. **Official GitHub Action** for install + SSH  

Those three delete most of the custom surface mvdmio-suite added in `.github/actions/kamal-deploy` and the secrets workaround for `DEPLOYED_COMMIT_SHA`. Built-in transient SSH retry is the next biggest win after that.

## Concrete friction already in mvdmio-suite (reference)

After the migration, the suite carries:

- Custom SSH key write in `.github/actions/kamal-deploy` and `deploy.reboot-proxy.yml`
- `DEPLOYED_COMMIT_SHA` via `.kamal/secrets-common` command substitution + base `env.secret` (no longer a builder arg)
- Log-scraping retry loop around `kamal deploy`
- Tool version pinned in `dotnet-tools.json`, read with Python in CI to install the global tool

Each of those is a direct reaction to a gap above.

## Open questions

- Prefer **ssh-agent** vs **`KAMAL_SSH_PRIVATE_KEY` only** for the first cut? Agent matches ecosystem; env var is smaller and enough for GHA secrets.
- Is limited `${ENV}` expansion acceptable as a permanent deviation, or should dynamic values be forced into secrets forever?
- Should the official GHA action live in this repo (`mvdmio/kamal.net/actions/…`) or a separate action repo?
- How aggressive should default connect retries be (off vs 2–3 with backoff)?
