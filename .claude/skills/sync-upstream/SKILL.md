---
name: sync-upstream
description: Mirror a new upstream Kamal release (basecamp/kamal) into this .NET port.
disable-model-invocation: true
---

# Sync upstream

This repo is a port: every behaviour in `src/Kamal` **mirrors** a Ruby original in `basecamp/kamal`. A sync closes the **drift** that opens when upstream ships a release. Work from released tags only — upstream `main` moves.

## 1. Gate on the version

```
gh api repos/basecamp/kamal/releases/latest --jq .tag_name
```

The port's baseline is `KamalVersion` in `src/Kamal/Configuration/KamalConfiguration.cs`.

Equal → the sync ends here. Report the version and that the port is current.
Newer → call that tag `NEW` and the baseline `OLD`, and continue. Several releases ahead: sync to the newest in one pass, reading every intervening release's notes.

**Done when** you have named OLD and NEW, or stopped.

## 2. Read what upstream did

Release notes, then the commit narrative, then the file list:

```
gh api repos/basecamp/kamal/releases/tags/vNEW --jq .body
gh api "repos/basecamp/kamal/compare/vOLD...vNEW" --jq '.commits[].commit.message | split("\n")[0]'
gh api "repos/basecamp/kamal/compare/vOLD...vNEW" --jq '.files[] | "\(.status)\t\(.filename)"'
```

Quote any URL holding `?` or `...` — fish treats them as globs and the call fails before `gh` sees it.

Per-file diff hunk: append `--jq '.files[] | select(.filename=="<path>") | .patch'` to the compare call.
Whole file at a tag: `gh api "repos/basecamp/kamal/contents/<path>?ref=vNEW" --jq .content | base64 -d`

**Done when** you hold the commit messages and the complete changed-file list.

## 3. Give every changed file a verdict

Each path from step 2 gets exactly one of:

- **mirror** — Ruby behaviour with a C# counterpart. Name the counterpart using the map below.
- **mirrored** — the port already carries this change. The port was built from upstream `main`, so it runs ahead of its declared version; read the C# before concluding a change is missing.
- **deviation** — the port deliberately excludes it. README's *Known deviations from Ruby Kamal* is the list; if this release introduces a new one, it gets added there in step 5.
- **n/a** — Ruby packaging or CI with no port surface: `Gemfile*`, `bin/`, `.github/`, `CODE_OF_CONDUCT.md`, `test/integration/**`, `test/fixtures/**`.

**Done when** every path carries a verdict and each **mirror** names its target C# file.

## 4. Mirror each change

Take one **mirror** item at a time, source and test together.

- Read the whole Ruby file at `vNEW`, not only the hunk — a hunk hides the method it sits in and the port needs the method.
- **The upstream test is the spec.** Changes under `test/**` pin the exact command strings the port must emit; port those assertions into the matching `tests/Kamal.Tests` file rather than inventing new ones.
- `configuration/docs/*.yml` and `cli/templates/**` copy **verbatim** — they ship as embedded resources and `kamal docs` prints them byte for byte.
- An upstream file with no counterpart yet gets a new C# file in the mapped folder, plus its test file.

**Done when** every **mirror** item is applied in `src/Kamal` and covered in `tests/Kamal.Tests`.

## 5. Bump the version

`vNEW` lands in three places:

- `<Version>` in `src/Kamal/Kamal.csproj`
- `KamalVersion` in `src/Kamal/Configuration/KamalConfiguration.cs`
- `README.md` — the intro line and the command-surface heading

Version-string assertions under `tests/Kamal.Tests/Cli/` follow. Any new **deviation** from step 3 gets written into README's deviations list.

**Done when** `grep -rn "<OLD>" src tests README.md --include="*.cs" --include="*.csproj" --include="*.md"` leaves only arbitrary version literals — `KamalUtilsTests` compares strings like `"2.11.0"` to exercise `OlderVersion`, and those stay put.

## 6. Verify and report

`dotnet test` runs green.

Then report: the new version, what was mirrored, and every file that was *not* — each **deviation**, **mirrored**, and **n/a** verdict with its one-line reason. What the sync skipped is the part the user cannot see from the diff.

**Done when** the suite is green and the report accounts for every file from step 2.

## Where upstream code lands

| basecamp/kamal | this port |
|---|---|
| `lib/kamal/cli/<x>.rb` | `src/Kamal/Cli/<X>Cli.cs` (`base.rb` → `CliBase.cs`) |
| `lib/kamal/cli/app/boot.rb` | `src/Kamal/Cli/AppBoot.cs` |
| `lib/kamal/cli/healthcheck/*.rb` | `src/Kamal/Cli/Healthcheck.cs` |
| `lib/kamal/cli/templates/**` | `src/Kamal/Templates/**` |
| `lib/kamal/commander.rb`, `commander/specifics.rb` | `src/Kamal/Commander.cs`, `Commander.Specifics.cs` |
| `lib/kamal/commands/<x>/<y>.rb` | `src/Kamal/Commands/<X>.<Y>.cs` |
| `lib/kamal/configuration/**` | `src/Kamal/Configuration/**` (`env/tag.rb` → `EnvTag.cs`, `proxy/run.rb` → `ProxyRun.cs`) |
| `lib/kamal/configuration/validator{,/*}.rb` | `src/Kamal/Configuration/Validation/` |
| `lib/kamal/secrets/**` | `src/Kamal/Secrets/**` |
| `lib/kamal/{utils,tags,git,env_file}.rb`, `utils/sensitive.rb` | `src/Kamal/Utils/` |
| `lib/kamal/sshkit_with_ext.rb` and SSHKit usage | `src/Kamal/Execution/` |
| `lib/kamal/output/*.rb` | `src/Kamal/Output/` |
| `test/<area>/<x>_test.rb` | `tests/Kamal.Tests/<Area>/<X>Tests.cs` |

Ruby `snake_case` → C# `PascalCase`. A Ruby module split across `foo/bar.rb` becomes a `Foo.Bar.cs` partial of `Foo.cs`.
