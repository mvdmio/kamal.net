# Kamal.NET

A C# port of [Kamal](https://github.com/basecamp/kamal), distributed as a dotnet
tool (`mvdmio.Kamal`, command `kamal`).

## Versioning

The version has two halves that answer to different owners:

- **Major and minor track upstream Kamal.** `2.11.x` means this port is faithful
  to Kamal 2.11. These digits move only when the port is brought up to a new
  upstream release — see `.claude/skills/sync-upstream`.
- **The patch component belongs to this repo alone.** It does not follow, mirror,
  or correspond to upstream's patch releases. It counts changes to the port
  itself: bug fixes in the C# code, packaging changes, anything with no upstream
  counterpart.

So Kamal.NET 2.11.1 and Ruby Kamal 2.11.1 are unrelated releases that happen to
share a number. Only the `2.11` part carries a shared meaning. Never infer an
upstream version from our patch digit, or bump the patch digit to match one.

A fix to ported code is a patch bump. Reaching a new upstream minor is a minor
bump, and the patch resets to `0`.

## Cutting a release

1. Bump `<Version>` in `src/Kamal/Kamal.csproj`.
2. Move the `## [Unreleased]` entries in `CHANGELOG.md` under a
   `## [<version>] - <date>` heading, and add the matching link references at the
   bottom of the file.
3. Commit, then `git tag v<version>` and push the tag.

The [release workflow](.github/workflows/release.yml) refuses to run if the tag
and the csproj version disagree. It takes the release notes verbatim from the
changelog section matching that version, so an absent or misnamed section ships
an empty release. A tag with a prerelease suffix (`v2.12.0-rc.1`) is marked as a
prerelease.

## Layout

- `src/Kamal` — the tool. `Execution/` holds the backends (`LocalBackend`,
  `SshBackend`) behind `IBackend`.
- `tests/Kamal.Tests` — the suite. Run it with `dotnet test`.
- `Kamal.slnx` — the solution.

The default branch is `main`.
