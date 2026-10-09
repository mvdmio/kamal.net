# Remove the kamal-proxy route on accessory stop and remove

Status: ready-for-agent
Category: bug

## Problem Statement

An operator runs `kamal accessory stop <name>` or `kamal accessory remove <name>` on an accessory that has a `proxy:` block. The command stops the container, but kamal-proxy keeps the accessory's route. kamal-proxy then sends traffic for the accessory's host to a container that is stopped or gone.

This affects Kamal.NET 2.11.3 and 2.12.0. It was first reported as GitHub issue mvdmio/kamal.net#2.

Triage reproduced it. For a proxied accessory, both commands run these steps on the accessory's host:

1. `docker container stop app-<name>`
2. `docker container ls --filter 'name=^app-<name>$' --quiet`

Neither command then runs `docker exec kamal-proxy kamal-proxy remove app-<name>`.

The cause is in `AccessoryCli.Stop`. It stops the container first. Then it looks up the *running* container. It removes the route only when that lookup returns a container ID. The container has just stopped, so the lookup always returns nothing, and the route is never removed.

Upstream Kamal has the same order, but it guards the removal with Ruby's `if target`. In Ruby an empty string counts as true, so upstream always runs `kamal-proxy remove`. The port's check for a non-empty string turned that always-true guard into an always-false one.

`accessory remove` calls the same stop step, so it has the same bug. `accessory restart` and `accessory reboot` also call it, but they boot or start the accessory again afterwards. That step deploys the route again, so those two commands do not leave a stale route.

## Solution

`accessory stop` and `accessory remove` look up the accessory's running container *before* they stop it. When that lookup finds a container, they remove the accessory's route from kamal-proxy after the stop. When it finds none, they skip the route removal and finish normally.

After either command, kamal-proxy has no route for an accessory that was running when the command started.

This departs from upstream on purpose. Upstream always runs `kamal-proxy remove`. kamal-proxy answers with `Error: service not found` and a non-zero exit when the route does not exist. Upstream then aborts the whole command, as reported in basecamp/kamal#1533 (open since April 2025). One common way to hit this: run `accessory stop`, then `accessory remove`. A faithful copy of upstream would bring that bug into the port. The README's list of known deviations records the difference.

## User Stories

1. As an operator, I want `kamal accessory stop <name>` to remove the accessory's kamal-proxy route, so that kamal-proxy stops sending traffic to a stopped container.
2. As an operator, I want `kamal accessory remove <name>` to remove the accessory's kamal-proxy route, so that no route is left pointing at a container that no longer exists.
3. As an operator, I want `kamal accessory remove all` to remove the route of every proxied accessory that was running, so that removing all accessories leaves kamal-proxy clean.
4. As an operator, I want `kamal accessory stop` on an accessory that is already stopped to finish without an error, so that running it twice is safe.
5. As an operator, I want `kamal accessory remove` to finish after an earlier `kamal accessory stop`, so that I do not hit upstream's "service not found" abort (basecamp/kamal#1533).
6. As an operator with an accessory on several hosts, I want each host's route handled by that host's own lookup, so that a host where the container was running loses its route and a host where it was not running is left alone.
7. As an operator, I want an accessory without a `proxy:` block to stop and be removed exactly as it is today, so that the fix does not touch accessories that kamal-proxy does not route.
8. As an operator, I want `kamal accessory restart` and `kamal accessory reboot` to end with a working route, so that the fix does not break the commands that stop and then start an accessory.
9. As an operator, I want a failed `kamal-proxy remove` to stop the command with an error when the accessory was running, so that a real kamal-proxy fault is not hidden.
10. As an operator reading the README, I want this difference from upstream listed under known deviations, so that I know why Kamal.NET behaves differently here.
11. As an operator reading the changelog, I want the fix listed under the next release, so that I know which version stops leaving routes behind.

## Implementation Decisions

- The change is confined to the stop step of the accessory CLI, `AccessoryCli.Stop`. `remove`, `remove all`, `restart` and `reboot` all reach it, so they all get the fix with no further change.
- For an accessory that runs behind the proxy, the stop step on each host runs in this order:
  1. Record the audit entry, as today.
  2. Look up the running container for the accessory's service name. This is the same `ContainerIdFor(containerName: ServiceName, onlyRunning: true)` lookup that runs today, moved to before the stop.
  3. Stop the container, as today, without failing on a non-zero exit.
  4. If the lookup in step 2 returned a container ID, run the accessory's proxy remove command (`docker exec kamal-proxy kamal-proxy remove <service-name>`, where the service name is the accessory's `service:` value or, by default, `<service>-<name>`).
- Apart from moving the lookup earlier, the commands and their order match upstream. The route is still removed after the container stops, as upstream does. Moving it before the stop, as the app's own stop command does, is a separate change.
- The proxy remove command keeps upstream's error handling: a non-zero exit fails the command. The new guard already skips the case where no route is expected, so a failure here points to a real fault.
- For an accessory with no `proxy:` block, the stop step does not change. It runs no lookup and no proxy command.
- The `Unreleased` section of the changelog gets a `Fixed` entry. It says that `accessory stop` and `accessory remove` now remove the kamal-proxy route of a proxied accessory, and it names mvdmio/kamal.net#2.
- The README's "Known deviations from Ruby Kamal" list gets one entry. It says that `kamal accessory stop` and `kamal accessory remove` remove the kamal-proxy route only when the accessory's container was running. It also says that upstream always tries and aborts with "service not found" when no route exists, and links basecamp/kamal#1533.

## Testing Decisions

- A good test drives the CLI through the existing test harness and asserts on the commands sent to the host. It does not inspect the code's internal state.
- The harness fake does not track container state. It answers the running-container lookup the same way before and after `docker container stop`. A test that only checks for `kamal-proxy remove` would therefore pass on today's broken code. Each test that expects a route removal must also check that the lookup ran *before* `docker container stop` in that host's command list. The harness keeps each host's commands in the order they ran.
- Tests to add to the accessory CLI tests, using an accessory with a `proxy:` block on its own host:
  - `accessory stop`, with the lookup answering a container ID: exit code 0. The host runs `docker exec kamal-proxy kamal-proxy remove app-<name>`, and the lookup comes before `docker container stop app-<name>`.
  - `accessory remove <name> -y`, with the lookup answering a container ID: exit code 0. The host runs the proxy remove command, in the same order as above. It also runs the container prune, image removal and directory removal that the existing remove test checks.
  - `accessory stop`, with the lookup answering nothing: exit code 0, and no `kamal-proxy remove` command on the host.
- Prior art: in the accessory CLI tests, the existing remove test checks the commands of `accessory remove -y`. The test for skipping hosts that already have a container answers a docker command with a canned response. The harness's response method matches on a command fragment.

## Out of Scope

- Cleaning up routes that 2.11.3 or 2.12.0 already left behind for an accessory whose container no longer runs. The new guard skips those. The workaround still applies: run `docker exec kamal-proxy kamal-proxy remove <service-name>` on the host, where the service name defaults to `<service>-<name>`.
- Removing the route before the container stops, so that kamal-proxy drains traffic first. Upstream does not do this for accessories.
- Reporting the fix upstream or changing upstream Kamal.
- The app's `stop` command. Its port already matches upstream's `endpoint.present?` check.
- Cutting the release. A fix to ported code is a patch bump, which `CLAUDE.md` describes.

## Further Notes

- Seen in a real run on 2026-10-08 with `kamal accessory remove <name> -d <destination> -y` on 2.11.3. The run printed the container stop, the container lookup, the prune, the image removal and the directory removal. No `kamal-proxy remove` line appeared.
- Upstream code compared: `lib/kamal/cli/accessory.rb`, `stop`, at the v2.11.0 tag and on `main`. Both use `execute *accessory.remove if target`. Upstream has no test for this path.
- kamal-proxy's remove command returns `service not found` with a non-zero exit when the service does not exist. That is why upstream's always-run behaviour aborts in basecamp/kamal#1533.
- The GitHub issue mvdmio/kamal.net#2 is the original report. This tracker does not close it. Close it by hand when the fix ships.

## Comments
