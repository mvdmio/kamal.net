# 06 — GitHub Actions and CI documentation

Status: done

## What to build

Suite-shaped consumers drop custom install/PATH glue, key-write steps, and ad-hoc docs by using official Actions and one CI doc entry.

**Actions (this repository, track tool releases by tag):**

- `actions/setup` — pin-able `mvdmio.Kamal` version, PATH, SSH from inputs (private key and/or agent-oriented inputs). Usable alone for non-deploy `kamal` commands.
- `actions/deploy` — composes setup, then `kamal deploy` with destination and passthrough args (multi-destination suite pattern).

**CI documentation:** `kamal docs ci` (or equivalent dedicated section) covering in one place:

- tool install and version pin
- SSH (agent, `KAMAL_SSH_PRIVATE_KEY`, `key_data` as first-class)
- config expansion and migrating-from-ERB guidance
- destinations
- failure classes and exit-code table (exact integers from step 04)
- retry flag
- GitHub Actions example

Docs smoke: the section returns the CI content; exit-code table matches the public contract. Prefer lightweight validation of Action metadata (`action.yml` inputs/outputs); full E2E deploy on real hosts is out of band.

Also update root README known-deviations where SSH/ERB statements are now wrong or incomplete for released behaviour.

## Footprint

Projects: `src/Kamal`, `tests/Kamal.Tests` (docs smoke), repo root (Actions)

- `actions/setup/` — new Action metadata and scripts
- `actions/deploy/` — new Action metadata and scripts
- `src/Kamal/Configuration/Docs/` — `ci` section content (or chosen embedding path)
- `src/Kamal/Configuration/Validation/ValidationDocs.cs` / `src/Kamal/Cli/MainCli.cs` — `docs` command surface
- `src/Kamal/Cli/KamalCli.cs` — docs section wiring if needed
- `README.md` — deviations / CI pointer
- `tests/Kamal.Tests/` — docs section smoke; optional action.yml shape checks

## Acceptance criteria

- [ ] `actions/setup` installs a pin-able tool version, puts it on PATH, and can configure SSH from inputs
- [ ] `actions/deploy` runs setup then `kamal deploy` with destination and extra args
- [ ] Consumers reference this repo’s action paths at a version tag (documented)
- [ ] `kamal docs ci` (or chosen name) returns install, SSH, expansion/ERB migration, destinations, failure classes/exit codes, retry, and a GHA example
- [ ] Exit-code table in docs matches the codes shipped in step 04
- [ ] `key_data` remains documented as a first-class path
- [ ] Action metadata validates at a lightweight level; docs smoke tests pass; `dotnet test` green

## Outcome

Published composite Actions `actions/setup` and `actions/deploy` (action.yml + bash scripts). Setup: pin-able `mvdmio.Kamal` via `dotnet tool install/update -g`, `~/.dotnet/tools` on `GITHUB_PATH`, optional `ssh-private-key` → `KAMAL_SSH_PRIVATE_KEY` multiline env (no key file). Inputs also `dotnet-version` (default `10.0.x`), `skip-dotnet-setup`. Deploy reuses sibling setup scripts via `github.action_path`, then `kamal deploy` with `destination`, `retry` (`--retry N`), `args`, `working-directory`. Consumers pin `mvdmio/kamal.net/actions/{setup,deploy}@vX.Y.Z` matching the tool version. Added embedded `Configuration/Docs/ci.yml` for `kamal docs ci` covering install, SSH (agent / env / key_data), expansion + ERB migration, destinations, failure exit codes (1/10/11/20/30/40 matching step 04), `--retry`, GHA examples. README: CI section + corrected SSH/ERB deviations. Tests: ValidationDocs/MainCli ci smoke, exit-code table vs `FailureClasses`, `GitHubActionsMetadataTests`. `dotnet test`: 835 passed. Footprint matched.
