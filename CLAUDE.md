# Kamal.NET

A C# port of [Kamal](https://github.com/basecamp/kamal), distributed as a dotnet
tool (`mvdmio.Kamal`, command `kamal`).

## Versioning

- **Major and minor track upstream Kamal.** `2.11.x` means this port is faithful
  to Kamal 2.11. These digits move only when the port is brought up to a new
  upstream release — see `.claude/skills/sync-upstream`. A new upstream minor
  resets the patch to `0`.
- **The patch component belongs to this repo alone.** It counts changes to the
  port itself: bug fixes in the C# code, packaging changes, anything with no
  upstream counterpart. Kamal.NET 2.11.1 and Ruby Kamal 2.11.1 are unrelated:
  read the upstream version from `2.11` alone. Never infer it from our patch
  digit, or bump the patch to match upstream's.

## Cutting a release

1. Bump `<Version>` in `src/Kamal/Kamal.csproj`.
2. Move the `## [Unreleased]` entries in `CHANGELOG.md` under a
   `## [<version>] - <date>` heading, and add the matching link references at the
   bottom of the file.
3. Commit, then `git tag v<version>` and push the tag.

The [release workflow](.github/workflows/release.yml) refuses to run if the tag
and the csproj version disagree. It takes the release notes verbatim from the
changelog section matching that version. An absent or misnamed section falls
back to GitHub's auto-generated notes, with only a workflow warning.
