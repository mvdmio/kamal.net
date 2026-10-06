---
name: release
description: Versioning policy and release procedure. Use when cutting a release, or when picking a Kamal.NET version number or mapping one to upstream Kamal.
---

# Release

Releases are cut by pushing a tag. The release workflow (`.github/workflows/release.yml`) then runs the tests, pushes `mvdmio.Kamal` to NuGet and opens the GitHub release.

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

## 1. Bump the version

Set `<Version>` in `src/Kamal/Kamal.csproj` to the new version, chosen under the policy above.

**Done when** `<Version>` holds the new version. The workflow refuses to run when the tag and `<Version>` disagree.

## 2. Close the changelog section

Move the `## [Unreleased]` entries in `CHANGELOG.md` under a `## [<version>] - <date>` heading, and add the matching link references at the bottom of the file.

The workflow takes the release notes verbatim from the section matching the version. An absent or misnamed section falls back to GitHub's auto-generated notes, with only a workflow warning.

**Done when** the `## [<version>] - <date>` heading sits over the former Unreleased entries, a `[<version>]` link reference exists at the bottom, and `[Unreleased]` compares from `v<version>`.

## 3. Tag and push

Commit, then tag and push the tag:

```
git tag v<version>
git push origin v<version>
```

**Done when** `v<version>` is on `origin`.
