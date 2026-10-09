# 01 — Remove the kamal-proxy route on accessory stop and remove

Status: pending
Depends on: none

## What to build

An operator runs `kamal accessory stop <name>` or `kamal accessory remove <name> -y` on an accessory that has a `proxy:` block. On each of the accessory's hosts where its container was running when the command started, kamal-proxy loses the accessory's route. On a host where the container was not running, the command skips the route removal and finishes normally. `accessory remove all`, `accessory restart` and `accessory reboot` reach the same stop step, so they get the fix with no change of their own. Restart and reboot still end with a working route, because their start or boot step deploys it again.

The whole code change is in the stop step of the accessory CLI. For an accessory that runs behind the proxy, each host runs, in this order:

1. The audit entry, as today.
2. The running-container lookup for the accessory's service name (`docker container ls --filter 'name=^<service-name>$' --quiet`). This is today's lookup, moved to before the stop.
3. `docker container stop <service-name>`, as today, without failing on a non-zero exit.
4. Only when step 2 returned a container ID: `docker exec kamal-proxy kamal-proxy remove <service-name>`. A non-zero exit from this command fails the command, as today.

The lookup and the removal stay inside the per-host block, so each host decides from its own lookup. An accessory with no `proxy:` block runs no lookup and no proxy command, exactly as today.

This departs from upstream on purpose. Upstream always runs `kamal-proxy remove` and aborts with `service not found` when no route exists (basecamp/kamal#1533). Document the departure in two places that the Spec names:

- **README**: one entry in the "Known deviations from Ruby Kamal" list. It says that `kamal accessory stop` and `kamal accessory remove` remove the kamal-proxy route only when the accessory's container was running. It says that upstream always tries and aborts with "service not found" when no route exists, and it links basecamp/kamal#1533.
- **Changelog**: a `### Fixed` entry under `## [Unreleased]` in `CHANGELOG.md`. It says that `accessory stop` and `accessory remove` now remove the kamal-proxy route of a proxied accessory, and it names #2. The Spec names this entry in the repo's own Keep a Changelog format, which the release workflow reads, so this step writes it.

Tests go in the accessory CLI tests. Add a deploy config with an accessory that has a `proxy:` block (for example `proxy: { host: <name>.example.com }`) on its own host. The harness fake does not track container state: it answers the lookup the same way before and after the stop. So every test that expects a route removal must also check, in that host's ordered command list, that the lookup ran before `docker container stop <service-name>`. A check for `kamal-proxy remove` alone passes on today's broken code.

## Footprint

Projects: `src/Kamal/Kamal.csproj`, `tests/Kamal.Tests/Kamal.Tests.csproj`

- `src/Kamal/Cli/AccessoryCli.cs` — `Stop` (the only behaviour change); `RemoveAccessory`, `Remove`, `Restart`, `Reboot`, `Start`, `Boot` reach or follow it and stay unchanged
- `src/Kamal/Commands/Accessory.Proxy.cs` — `Remove` (the proxy remove command, read only)
- `src/Kamal/Commands/CommandsBase.cs` — `ContainerIdFor` (the lookup command, read only)
- `tests/Kamal.Tests/Cli/AccessoryCliTests.cs` — new proxied-accessory deploy config and three new tests; prior art: `RemoveWithConfirmationRemovesEverything`, `BootSkipsHostsWithExistingContainer`
- `tests/Kamal.Tests/Cli/CliTestHarness.cs` — `RespondTo`, `CommandsOn` (used, not changed)
- `README.md` — "Known deviations from Ruby Kamal" list
- `CHANGELOG.md` — `## [Unreleased]` section

## Acceptance criteria

- [ ] For a proxied accessory, `AccessoryCli.Stop` runs the running-container lookup before `docker container stop`, and runs `kamal-proxy remove` after the stop only when that lookup returned a container ID.
- [ ] The `kamal-proxy remove` command keeps its default error handling: a non-zero exit fails the command.
- [ ] For an accessory with no `proxy:` block, the stop step runs no lookup and no proxy command.
- [ ] Test: `accessory stop <name>` with the lookup answering a container ID exits 0. The host runs `docker exec kamal-proxy kamal-proxy remove app-<name>`, and the lookup comes before `docker container stop app-<name>` in that host's commands.
- [ ] Test: `accessory remove <name> -y` with the lookup answering a container ID exits 0. The host runs the proxy remove command with the same ordering check. It also runs the container prune, image removal and directory removal that `RemoveWithConfirmationRemovesEverything` checks.
- [ ] Test: `accessory stop <name>` with the lookup answering nothing exits 0, and the host runs no `kamal-proxy remove` command.
- [ ] The README's "Known deviations from Ruby Kamal" list has the entry described above, with a link to basecamp/kamal#1533.
- [ ] `CHANGELOG.md` has a `### Fixed` entry under `## [Unreleased]` that names #2.
- [ ] `dotnet test` passes for the whole solution.
